package com.hapbeat.demoremote.mirror

import android.media.MediaCodec
import android.media.MediaFormat
import android.os.Build
import android.os.SystemClock
import android.view.Surface
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

/**
 * Display-only Quest mirror: pushes scrcpy-server, starts it over the shared dadb connection,
 * reads the H.264 stream and renders it to [surface] with MediaCodec. No audio, no input.
 */
class MirrorSession(
    private val dadb: Dadb,
    private val serverJar: () -> InputStream,
    private val settings: MirrorSettings,
    private val surface: Surface,
    private val listener: Listener,
) {
    interface Listener {
        fun onVideoSize(width: Int, height: Int)
        fun onError(message: String)
    }

    private class RestartNeeded : Exception()

    @Volatile private var stopped = false
    private val codecLock = Any()
    private var codec: MediaCodec? = null
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
                SystemClock.sleep(100)
            }
        }
        val opened = stream ?: throw IOException("scrcpy に接続できません")
        video = opened
        decode(ScrcpyVideoReader(opened.source.inputStream()))
    }

    private fun decode(reader: ScrcpyVideoReader) {
        val codecId = reader.readHeader()
        if (codecId != ScrcpyVideoReader.CODEC_H264) throw IOException("未対応のコーデック 0x%08x".format(codecId))
        val info = MediaCodec.BufferInfo()
        var dropping = false
        var droppingSince = 0L
        while (!stopped) {
            when (val packet = reader.readPacket()) {
                is ScrcpyPacket.Session -> {
                    configureCodec(packet.width, packet.height)
                    listener.onVideoSize(packet.width, packet.height)
                    dropping = false
                }
                is ScrcpyPacket.Media -> {
                    if (dropping && !packet.keyFrame && !packet.config) {
                        if (SystemClock.uptimeMillis() - droppingSince > KEYFRAME_WAIT_MS) throw RestartNeeded()
                        drainOutput(info)
                        continue
                    }
                    val queued = queue(packet)
                    // A lost SPS/PPS cannot be recovered from later frames: restart for a fresh one.
                    if (!queued && packet.config) throw RestartNeeded()
                    if (!queued) {
                        if (!dropping) { dropping = true; droppingSince = SystemClock.uptimeMillis() }
                    } else if (packet.keyFrame) {
                        dropping = false
                    }
                    drainOutput(info)
                }
            }
        }
    }

    private fun configureCodec(width: Int, height: Int) = synchronized(codecLock) {
        if (stopped) return
        codec?.let { it.stop(); it.release() }
        val format = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, width, height)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) format.setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
        codec = MediaCodec.createDecoderByType(MediaFormat.MIMETYPE_VIDEO_AVC).apply {
            configure(format, surface, null, 0)
            start()
        }
    }

    /** Returns false if no input buffer became free within 50 ms (decoder is congested). */
    private fun queue(packet: ScrcpyPacket.Media): Boolean = synchronized(codecLock) {
        val c = codec ?: return true // no session yet: nothing to decode into
        val index = c.dequeueInputBuffer(INPUT_TIMEOUT_US)
        if (index < 0) return false
        val buffer = c.getInputBuffer(index) ?: return false
        buffer.clear()
        buffer.put(packet.data)
        var flags = 0
        if (packet.config) flags = flags or MediaCodec.BUFFER_FLAG_CODEC_CONFIG
        if (packet.keyFrame) flags = flags or MediaCodec.BUFFER_FLAG_KEY_FRAME
        c.queueInputBuffer(index, 0, packet.data.size, if (packet.config) 0 else packet.ptsUs, flags)
        true
    }

    private fun drainOutput(info: MediaCodec.BufferInfo) = synchronized(codecLock) {
        val c = codec ?: return
        while (true) {
            val index = c.dequeueOutputBuffer(info, 0)
            if (index >= 0) c.releaseOutputBuffer(index, true)
            else if (index == MediaCodec.INFO_TRY_AGAIN_LATER) break
            // INFO_OUTPUT_FORMAT_CHANGED / INFO_OUTPUT_BUFFERS_CHANGED: keep draining
        }
    }

    private fun releaseCodec() = synchronized(codecLock) {
        codec?.let {
            try {
                it.stop()
            } catch (_: IllegalStateException) {
                // Already in error/released state; release() below still frees it.
            }
            it.release()
        }
        codec = null
    }

    private fun closeStreams() {
        video?.let { runCatching { it.close() } }
        video = null
        shell?.let { runCatching { it.close() } }
        shell = null
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
        const val INPUT_TIMEOUT_US = 50_000L
        const val KEYFRAME_WAIT_MS = 2_000L
    }
}
