package com.hapbeat.demoremote.mirror

import com.hapbeat.demoremote.data.MirrorSettings
import dadb.AdbShellPacket
import dadb.AdbShellStream
import dadb.AdbStream
import dadb.Dadb
import okio.source
import java.io.IOException
import java.io.InputStream
import java.security.SecureRandom

/** Pure helpers for the scrcpy server launch (unit-tested). */
object ScrcpyLaunch {
    const val REMOTE_JAR = "/data/local/tmp/hapbeat-remote-scrcpy.jar"
    const val SERVER_VERSION = "4.1"

    /** Positive 31-bit id formatted as 8 hex digits (server parses it with Integer.parseInt(value, 16)). */
    fun newScid(random: SecureRandom = SecureRandom()): String = "%08x".format(random.nextInt() and 0x7fffffff)

    /** Parses `wm size`; an Override size wins over the Physical size. */
    fun parseWmSize(output: String): Pair<Int, Int>? {
        val re = Regex("""(Physical|Override) size:\s*(\d+)x(\d+)""")
        val sizes = re.findAll(output).associate { it.groupValues[1] to (it.groupValues[2].toInt() to it.groupValues[3].toInt()) }
        return sizes["Override"] ?: sizes["Physical"]
    }

    /** Left eye only for side-by-side HMD panels (W > 1.2 H) unless both eyes are requested. */
    fun cropFor(width: Int, height: Int, bothEyes: Boolean): String? =
        if (!bothEyes && width > height * 1.2) "${width / 2}:$height:0:0" else null

    fun serverCommand(scid: String, settings: MirrorSettings, crop: String?): String = buildString {
        append("CLASSPATH=$REMOTE_JAR app_process / com.genymobile.scrcpy.Server $SERVER_VERSION")
        append(" scid=$scid log_level=warn tunnel_forward=true audio=false control=false cleanup=true")
        append(" video_codec=h264 max_size=${settings.maxSize} video_bit_rate=${settings.bitRate} max_fps=${settings.maxFps}")
        append(" send_device_meta=false stay_awake=false power_on=false")
        if (crop != null) append(" crop=$crop")
    }
}

/** Where a mirror session's H.264 goes. [MirrorSession] calls these one at a time. */
interface VideoSink {
    /** Stream (re)configuration: (re)creates the decoder for this size. */
    fun configure(width: Int, height: Int)
    /** Returns false when the decoder is congested and the packet was not taken. Before [configure]: nothing to decode into, true. */
    fun queue(packet: ScrcpyPacket.Media): Boolean
    /** Outputs what is decoded so far. */
    fun drain()
    /** Frees the decoder; safe to call repeatedly, and [configure] may follow. */
    fun release()
}

/**
 * Display-only Quest mirror: pushes scrcpy-server, starts it over the shared dadb connection,
 * reads the H.264 stream and hands it to [sink]. No audio, no input.
 */
class MirrorSession(
    /** Opens a dedicated adb connection: the video stream never shares (or stalls) the control one. */
    private val connect: () -> Dadb,
    private val serverJar: () -> InputStream,
    private val settings: MirrorSettings,
    private val sink: VideoSink,
    private val listener: Listener,
) {
    interface Listener {
        fun onVideoSize(width: Int, height: Int)
        fun onError(message: String)
    }

    private class RestartNeeded : Exception()

    @Volatile private var stopped = false
    @Volatile private var dadb: Dadb? = null
    private val sinkLock = Any()
    @Volatile private var shell: AdbShellStream? = null
    @Volatile private var video: AdbStream? = null
    private val shellTail = ArrayDeque<String>()
    private var thread: Thread? = null

    fun start() {
        thread = Thread({ runLoop() }, "quest-mirror").also { it.start() }
    }

    /** Stops decoding before the surface goes away and tears down the server. Safe to call repeatedly. */
    fun stop() {
        stopped = true
        releaseCodec()
        closeStreams()
    }

    private fun runLoop() {
        while (!stopped) {
            try {
                runSession()
                if (!stopped) listener.onError("ミラーの映像が終了しました")
                return
            } catch (_: RestartNeeded) {
                // Decoder fell behind and no keyframe arrived: restart the server to get one.
                releaseCodec()
                closeStreams()
            } catch (e: Exception) {
                if (!stopped) listener.onError(errorSummary(e))
                return
            } finally {
                if (stopped) { releaseCodec(); closeStreams() }
            }
        }
    }

    private fun runSession() {
        val dadb = connect().also { this.dadb = it }
        if (stopped) return
        val push = serverJar().use { input ->
            dadb.push(input.source(), ScrcpyLaunch.REMOTE_JAR, 0b110100100, System.currentTimeMillis())
        }
        if (push !is dadb.SyncResult.Success) throw IOException("scrcpy-server の転送に失敗しました")

        val size = ScrcpyLaunch.parseWmSize(dadb.shell("wm size").output) ?: throw IOException("wm size を取得できません")
        val crop = ScrcpyLaunch.cropFor(size.first, size.second, settings.bothEyes)
        val scid = ScrcpyLaunch.newScid()
        val serverShell = dadb.openShell(ScrcpyLaunch.serverCommand(scid, settings, crop))
        shell = serverShell
        Thread({ drainShell(serverShell) }, "scrcpy-shell").start()

        var stream: AdbStream? = null
        for (attempt in 0 until 50) {
            if (stopped) return
            try {
                stream = dadb.open("localabstract:scrcpy_$scid")
                break
            } catch (_: Exception) {
                Thread.sleep(100)
            }
        }
        val opened = stream ?: throw IOException("scrcpy に接続できません")
        video = opened
        decode(ScrcpyVideoReader(opened.source.inputStream()))
    }

    private fun decode(reader: ScrcpyVideoReader) {
        val codecId = reader.readHeader()
        if (codecId != ScrcpyVideoReader.CODEC_H264) throw IOException("未対応のコーデック 0x%08x".format(codecId))
        var dropping = false
        var droppingSince = 0L
        while (!stopped) {
            when (val packet = reader.readPacket()) {
                is ScrcpyPacket.Session -> {
                    configureSink(packet.width, packet.height)
                    listener.onVideoSize(packet.width, packet.height)
                    dropping = false
                }
                is ScrcpyPacket.Media -> {
                    if (dropping && !packet.keyFrame && !packet.config) {
                        if (uptimeMs() - droppingSince > KEYFRAME_WAIT_MS) throw RestartNeeded()
                        drainSink()
                        continue
                    }
                    val queued = synchronized(sinkLock) { sink.queue(packet) }
                    // A lost SPS/PPS cannot be recovered from later frames: restart for a fresh one.
                    if (!queued && packet.config) throw RestartNeeded()
                    if (!queued) {
                        if (!dropping) { dropping = true; droppingSince = uptimeMs() }
                    } else if (packet.keyFrame) {
                        dropping = false
                    }
                    drainSink()
                }
            }
        }
    }

    private fun configureSink(width: Int, height: Int) = synchronized(sinkLock) {
        if (stopped) return
        sink.configure(width, height)
    }

    private fun drainSink() = synchronized(sinkLock) { sink.drain() }

    private fun releaseCodec() = synchronized(sinkLock) { sink.release() }

    private fun uptimeMs() = System.nanoTime() / 1_000_000

    private fun closeStreams() {
        video?.let { runCatching { it.close() } }
        video = null
        shell?.let { runCatching { it.close() } }
        shell = null
        dadb?.let { runCatching { it.close() } }
        dadb = null
    }

    private fun drainShell(stream: AdbShellStream) {
        try {
            while (true) {
                when (val packet = stream.read()) {
                    is AdbShellPacket.Exit -> return
                    else -> {
                        val text = String(packet.payload, Charsets.UTF_8)
                        synchronized(shellTail) {
                            text.lines().filter { it.isNotBlank() }.forEach {
                                shellTail.addLast(it)
                                while (shellTail.size > 5) shellTail.removeFirst()
                            }
                        }
                    }
                }
            }
        } catch (_: Exception) {
            // Stream closed by stop() or server exit; the tail collected so far is kept for diagnostics.
        }
    }

    private fun errorSummary(e: Exception): String {
        val tail = synchronized(shellTail) { shellTail.joinToString(" / ") }
        val base = e.message ?: e.javaClass.simpleName
        return if (tail.isEmpty()) base else "$base（server: $tail）"
    }

    private companion object {
        const val KEYFRAME_WAIT_MS = 2_000L
    }
}
