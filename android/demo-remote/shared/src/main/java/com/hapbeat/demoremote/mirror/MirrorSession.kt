package com.hapbeat.demoremote.mirror

import com.hapbeat.demoremote.data.MirrorSettings
import dadb.AdbShellPacket
import dadb.AdbShellStream
import dadb.AdbStream
import dadb.Dadb
import okio.source
import java.io.IOException
import java.io.InputStream
import java.net.Socket
import java.security.SecureRandom
import kotlin.concurrent.thread

/** Pure helpers for the scrcpy server launch (unit-tested). */
object ScrcpyLaunch {
    /** One file per server: a starting server deletes its own jar, and phones and a PC may start theirs at the same moment. */
    fun remoteJar(scid: String): String = "/data/local/tmp/hapbeat-remote-scrcpy-$scid.jar"
    const val SERVER_VERSION = "4.1"
    /**
     * When the video is also relayed: a phone can only start at a keyframe. scrcpy tells the encoder 60 fps and encoders
     * commonly count the interval in frames, so at the mirror's 30 fps this is about every 2 s (scrcpy's default of 10: 20 s).
     */
    const val RELAY_KEY_FRAME_INTERVAL_S = 1

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

    fun serverCommand(scid: String, settings: MirrorSettings, crop: String?, keyFrameIntervalS: Int? = null): String = buildString {
        append("CLASSPATH=${remoteJar(scid)} app_process / com.genymobile.scrcpy.Server $SERVER_VERSION")
        append(" scid=$scid log_level=warn tunnel_forward=true audio=false control=false cleanup=true")
        append(" video_codec=h264 max_size=${settings.maxSize} video_bit_rate=${settings.bitRate} max_fps=${settings.maxFps}")
        append(" send_device_meta=false stay_awake=false power_on=false")
        if (crop != null) append(" crop=$crop")
        if (keyFrameIntervalS != null) append(" video_codec_options=i-frame-interval:int=$keyFrameIntervalS")
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

/** What a platform's mirror does about the relay ([MirrorRelayServer]). */
enum class MirrorRelayMode { HOST, JOIN }

/** What one session does about the relay. */
sealed interface RelayRole {
    data object None : RelayRole
    /** Takes a relaying PC's video when one can be reached; otherwise, or with no PC at all, its own from the Quest. */
    data object Join : RelayRole
    /** Offers its video to phones. [connectNote]: an adb connection with a read timeout, for the note on the Quest. */
    class Host(val questIp: String, val connectNote: () -> Dadb) : RelayRole
}

/** Where a session's video comes from. */
enum class MirrorRoute { DIRECT, VIA_PC, DIRECT_PC_UNREACHABLE, DIRECT_PC_TOO_HEAVY }

class RelayTiming(
    /** While on its own stream, how often a phone looks for a relay. */
    val watchMs: Long = 5_000L,
    /** After the PC's video ended, how long a phone waits before looking again: the PC may only be restarting. */
    val retryMs: Long = 2_000L,
)

/**
 * Display-only Quest mirror: reads the H.264 stream and hands it to [sink]. No audio, no input. The stream comes from a
 * scrcpy-server it starts over its own adb connection, or, as [RelayRole.Join], from a PC that relays its own.
 */
class MirrorSession(
    /** Opens a dedicated adb connection: the video stream never shares (or stalls) the control one. */
    private val connect: () -> Dadb,
    private val serverJar: () -> InputStream,
    private val settings: MirrorSettings,
    private val sink: VideoSink,
    private val listener: Listener,
    private val relayRole: RelayRole = RelayRole.None,
    private val relayTiming: RelayTiming = RelayTiming(),
) {
    interface Listener {
        fun onVideoSize(width: Int, height: Int)
        fun onError(message: String)
        fun onRoute(route: MirrorRoute) {}
        /** As host: how many phones take the video; null when the relay could not start (the PC's own picture still runs). */
        fun onRelayClients(count: Int?) {}
    }

    private class RestartNeeded : Exception()

    /** The decoder failed: with the PC's video, the way out is the phone's own lighter stream. */
    private class SinkFailed(cause: Exception) : Exception(cause.message ?: cause.javaClass.simpleName, cause)

    @Volatile private var stopped = false
    @Volatile private var dadb: Dadb? = null
    private val sinkLock = Any()
    @Volatile private var shell: AdbShellStream? = null
    @Volatile private var video: AdbStream? = null
    @Volatile private var relaySocket: Socket? = null
    /** The video ended on purpose or as expected, to fetch it another way: not an error. */
    @Volatile private var rerouting = false
    /** The PC's video ran and no picture has run since: failures now (a sleeping Quest, say) are not final. */
    @Volatile private var returningFromRelay = false
    /** This phone cannot play the PC's video: its own stream from now on. */
    @Volatile private var relayDeclined = false
    @Volatile private var route: MirrorRoute? = null
    private val relayRestarts = ArrayDeque<Long>()
    @Volatile private var relayServer: MirrorRelayServer? = null
    private val noteWake = Object()
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
        relayServer?.close()
        synchronized(noteWake) { noteWake.notifyAll() }
    }

    private fun runLoop() {
        while (!stopped) {
            var waitBeforeNext = false
            try {
                runSession()
                if (stopped) return
                if (!rerouting) {
                    listener.onError("ミラーの映像が終了しました")
                    return
                }
            } catch (_: RestartNeeded) {
                // Decoder fell behind and no keyframe arrived: start over to get one.
                noteRelayRestart()
            } catch (e: Exception) {
                if (stopped) return
                if (!rerouting) {
                    if (!returningFromRelay) {
                        listener.onError(errorSummary(e))
                        return
                    }
                    waitBeforeNext = true
                }
            } finally {
                rerouting = false
                releaseCodec()
                closeStreams()
            }
            if (waitBeforeNext) Thread.sleep(relayTiming.retryMs)
        }
    }

    /** A phone that keeps falling behind the PC's video takes its own, lighter stream instead. */
    private fun noteRelayRestart() {
        if (route != MirrorRoute.VIA_PC) return
        val now = uptimeMs()
        relayRestarts.addLast(now)
        while (now - relayRestarts.first() > RELAY_RESTART_WINDOW_MS) relayRestarts.removeFirst()
        if (relayRestarts.size >= RELAY_MAX_RESTARTS) relayDeclined = true
    }

    private fun runSession() {
        val dadb = connect().also { this.dadb = it }
        try {
            if (stopped) return
            var pcUnreachable = false
            if (relayRole == RelayRole.Join && !relayDeclined) {
                val relay = lookUpRelay(dadb)
                if (relay != null) {
                    if (runViaRelay(relay)) return
                    pcUnreachable = true
                }
            }
            runDirect(dadb, pcUnreachable)
        } finally {
            // Also when stop() ran before the field was set; a closed Dadb would otherwise reconnect on its next use.
            runCatching { dadb.close() }
        }
    }

    private fun lookUpRelay(dadb: Dadb): RelayInfo? = RelayNote.parse(dadb.shell(RelayNote.READ_COMMAND).output)

    /** The video from the PC. False: the PC cannot be reached. True: it ran and has ended, and [rerouting] is set. */
    private fun runViaRelay(relay: RelayInfo): Boolean {
        val socket = try {
            MirrorRelayClient.connect(relay)
        } catch (_: IOException) {
            return false
        }
        relaySocket = socket
        val reader = ScrcpyVideoReader(socket.getInputStream())
        val codecId = try {
            reader.readHeader()
        } catch (_: IOException) {
            closeRelaySocket()
            return false
        }
        // Nothing more to ask the Quest while this runs; the next look opens a new connection.
        closeAdb()
        requireH264(codecId)
        setRoute(MirrorRoute.VIA_PC)
        returningFromRelay = true
        try {
            decodePackets(reader, RELAY_KEYFRAME_WAIT_MS)
        } catch (_: IOException) {
            // The PC stopped, or nothing came for a while.
        } catch (_: SinkFailed) {
            relayDeclined = true
        }
        if (!stopped) {
            rerouting = true
            if (!relayDeclined) Thread.sleep(relayTiming.retryMs)
        }
        return true
    }

    private fun runDirect(dadb: Dadb, pcUnreachable: Boolean) {
        val scid = ScrcpyLaunch.newScid()
        val push = serverJar().use { input ->
            dadb.push(input.source(), ScrcpyLaunch.remoteJar(scid), 0b110100100, System.currentTimeMillis())
        }
        if (push !is dadb.SyncResult.Success) throw IOException("scrcpy-server の転送に失敗しました")

        val size = ScrcpyLaunch.parseWmSize(dadb.shell("wm size").output) ?: throw IOException("wm size を取得できません")
        val crop = ScrcpyLaunch.cropFor(size.first, size.second, settings.bothEyes)
        val keyFrameInterval = if (relayRole is RelayRole.Host) ScrcpyLaunch.RELAY_KEY_FRAME_INTERVAL_S else null
        val serverShell = dadb.openShell(ScrcpyLaunch.serverCommand(scid, settings, crop, keyFrameInterval))
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
        val reader = ScrcpyVideoReader(opened.source.inputStream())
        requireH264(reader.readHeader())
        returningFromRelay = false
        setRoute(
            when {
                relayDeclined -> MirrorRoute.DIRECT_PC_TOO_HEAVY
                pcUnreachable -> MirrorRoute.DIRECT_PC_UNREACHABLE
                else -> MirrorRoute.DIRECT
            },
        )
        (relayRole as? RelayRole.Host)?.let(::startHosting)
        val watch = if (relayRole == RelayRole.Join && !relayDeclined) watchForRelay(dadb) else null
        try {
            decodePackets(reader, KEYFRAME_WAIT_MS)
        } finally {
            watch?.interrupt()
        }
    }

    /** As soon as a relay can be used, cuts this session's own stream so that the Quest is back to encoding one. */
    private fun watchForRelay(dadb: Dadb): Thread = thread(name = "relay-watch", isDaemon = true) {
        try {
            while (!stopped) {
                Thread.sleep(relayTiming.watchMs)
                if (stopped) return@thread
                val relay = try {
                    lookUpRelay(dadb)
                } catch (_: Exception) {
                    continue
                }
                val usable = relay != null && MirrorRelayClient.reachable(relay)
                // Neither call above can be interrupted: if the stream ended meanwhile, what is open now is the next run's.
                if (Thread.currentThread().isInterrupted) return@thread
                when {
                    usable -> {
                        rerouting = true
                        closeStreams()
                        return@thread
                    }
                    relay != null -> setRoute(MirrorRoute.DIRECT_PC_UNREACHABLE)
                    else -> setRoute(MirrorRoute.DIRECT)
                }
            }
        } catch (_: InterruptedException) {
            // The stream ended first.
        }
    }

    private fun setRoute(value: MirrorRoute) {
        if (route == value) return
        route = value
        listener.onRoute(value)
    }

    /** Before the first packet is read: the session and config packets are what a phone that joins later starts from. */
    private fun startHosting(host: RelayRole.Host) {
        if (relayServer != null) return
        val server = try {
            val bindIp = localAddressTowards(host.questIp) ?: throw IOException("no route to ${host.questIp}")
            MirrorRelayServer(bindIp) { listener.onRelayClients(it) }
        } catch (_: Exception) {
            listener.onRelayClients(null)
            return
        }
        relayServer = server
        if (stopped) {
            server.close()
            return
        }
        listener.onRelayClients(0)
        thread(name = "relay-note", isDaemon = true) { keepRelayNote(host, server.info) }
    }

    /** Keeps the note on the Quest fresh, then removes it. On its own connection, so that busy video cannot make the relay look dead. */
    private fun keepRelayNote(host: RelayRole.Host, info: RelayInfo) {
        var adb: Dadb? = null
        while (!stopped) {
            try {
                val current = adb ?: host.connectNote().also { adb = it }
                current.shell(RelayNote.writeCommand(info))
            } catch (_: Exception) {
                adb?.let { runCatching { it.close() } }
                adb = null
            }
            synchronized(noteWake) { if (!stopped) noteWake.wait(RelayNote.REFRESH_MS) }
        }
        runCatching {
            val current = adb ?: host.connectNote().also { adb = it }
            current.shell(RelayNote.removeCommand(info))
        }
        adb?.let { runCatching { it.close() } }
    }

    private fun requireH264(codecId: Int) {
        if (codecId != ScrcpyVideoReader.CODEC_H264) throw IOException("未対応のコーデック 0x%08x".format(codecId))
    }

    private fun decodePackets(reader: ScrcpyVideoReader, keyframeWaitMs: Long) {
        var dropping = false
        var droppingSince = 0L
        while (!stopped) {
            val packet = reader.readPacket()
            relayServer?.onPacket(packet)
            when (packet) {
                is ScrcpyPacket.Session -> {
                    configureSink(packet.width, packet.height)
                    listener.onVideoSize(packet.width, packet.height)
                    dropping = false
                }
                is ScrcpyPacket.Media -> {
                    if (dropping && !packet.keyFrame && !packet.config) {
                        if (uptimeMs() - droppingSince > keyframeWaitMs) throw RestartNeeded()
                        drainSink()
                        continue
                    }
                    val queued = withSink { sink.queue(packet) }
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

    private inline fun <T> withSink(call: () -> T): T = try {
        synchronized(sinkLock) { call() }
    } catch (e: Exception) {
        throw SinkFailed(e)
    }

    private fun configureSink(width: Int, height: Int) = withSink {
        if (!stopped) sink.configure(width, height)
    }

    private fun drainSink() = withSink { sink.drain() }

    private fun releaseCodec() = synchronized(sinkLock) { sink.release() }

    private fun uptimeMs() = System.nanoTime() / 1_000_000

    private fun closeStreams() {
        video?.let { runCatching { it.close() } }
        video = null
        shell?.let { runCatching { it.close() } }
        shell = null
        closeRelaySocket()
        closeAdb()
    }

    private fun closeRelaySocket() {
        relaySocket?.let { runCatching { it.close() } }
        relaySocket = null
    }

    private fun closeAdb() {
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
        /** Well over the relayed stream's keyframe interval, also when its frame rate drops. */
        const val RELAY_KEYFRAME_WAIT_MS = 8_000L
        const val RELAY_RESTART_WINDOW_MS = 60_000L
        const val RELAY_MAX_RESTARTS = 3
    }
}
