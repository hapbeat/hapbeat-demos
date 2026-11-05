package com.hapbeat.demoremote.mirror

import java.io.ByteArrayOutputStream
import java.io.DataOutputStream
import java.io.IOException
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.security.MessageDigest
import java.security.SecureRandom
import kotlin.concurrent.thread

/*
 * Relay of the mirror's video from a PC to phones, so that the Quest encodes one stream however many screens show it.
 * The PC sends the packets on exactly as they came (no decoding or encoding for this), in the scrcpy framing that
 * ScrcpyVideoReader reads. A phone finds the relay through a note the PC keeps on the Quest: whoever may use adb on this
 * Quest can read it, so there is no pairing and no address to type. The stream is not encrypted, like Wi-Fi adb itself.
 * A phone with no PC around never sees a note and takes its video from the Quest as before.
 */

/** Where a PC's relay listens, and the secret that gets its video. */
data class RelayInfo(val host: String, val port: Int, val token: String)

/** The note on the Quest. Readable by the adb shell user only; its age is measured on the Quest's own clock. */
object RelayNote {
    const val PATH = "/data/local/tmp/hapbeat-remote-relay"
    const val REFRESH_MS = 10_000L
    const val MAX_AGE_SECONDS = 25L
    const val READ_COMMAND = "cat $PATH 2>/dev/null; echo; stat -c %Y $PATH 2>/dev/null; date +%s"

    private val IPV4 = Regex("""^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$""")
    private val TOKEN = Regex("""^[0-9a-f]{32}$""")

    fun writeCommand(info: RelayInfo): String = "umask 077; echo '${info.host} ${info.port} ${info.token}' > $PATH"

    /** Removes the note only if it is still this relay's. */
    fun removeCommand(info: RelayInfo): String = "grep -q ${info.token} $PATH 2>/dev/null && rm -f $PATH"

    /** The relay in [READ_COMMAND]'s output; null when there is none or its PC stopped refreshing it. */
    fun parse(output: String): RelayInfo? {
        val lines = output.lines().map { it.trim() }.filter { it.isNotEmpty() }
        if (lines.size != 3) return null
        val modified = lines[1].toLongOrNull() ?: return null
        val now = lines[2].toLongOrNull() ?: return null
        if (now - modified > MAX_AGE_SECONDS) return null
        val parts = lines[0].split(' ')
        if (parts.size != 3) return null
        val (host, portText, token) = parts
        val octets = IPV4.find(host)?.groupValues?.drop(1) ?: return null
        if (octets.any { it.toInt() > 255 }) return null
        val port = portText.toIntOrNull()?.takeIf { it in 1..65535 } ?: return null
        if (!TOKEN.matches(token)) return null
        return RelayInfo(host, port, token)
    }
}

/** What goes over the relay's socket. */
object RelayWire {
    const val MAX_HELLO_BYTES = 64

    /** Asks for the video. */
    fun hello(token: String): ByteArray = "HBRELAY1 $token\n".toByteArray(Charsets.US_ASCII)

    /** Asks whether the video could be had, without taking a place. */
    fun probe(token: String): ByteArray = "HBRELAY1P $token\n".toByteArray(Charsets.US_ASCII)

    /** The dummy byte and the codec id that [ScrcpyVideoReader.readHeader] reads. */
    fun streamHeader(): ByteArray = build { writeByte(0); writeInt(ScrcpyVideoReader.CODEC_H264) }

    fun encode(packet: ScrcpyPacket): ByteArray = when (packet) {
        is ScrcpyPacket.Session -> build {
            writeInt(ScrcpyVideoReader.SESSION_MARK)
            writeInt(packet.width)
            writeInt(packet.height)
        }
        is ScrcpyPacket.Media -> build {
            var ptsAndFlags = packet.ptsUs
            if (packet.config) ptsAndFlags = ptsAndFlags or ScrcpyVideoReader.FLAG_CONFIG
            if (packet.keyFrame) ptsAndFlags = ptsAndFlags or ScrcpyVideoReader.FLAG_KEY_FRAME
            writeLong(ptsAndFlags)
            writeInt(packet.data.size)
            write(packet.data)
        }
    }

    private inline fun build(body: DataOutputStream.() -> Unit): ByteArray {
        val bytes = ByteArrayOutputStream()
        DataOutputStream(bytes).body() // big-endian, as the reader expects
        return bytes.toByteArray()
    }
}

/** This PC's address on the network that reaches [ip]; nothing is sent. Null when there is no route. */
fun localAddressTowards(ip: String): String? = runCatching {
    DatagramSocket().use { socket ->
        socket.connect(InetAddress.getByName(ip), 9)
        socket.localAddress.takeUnless { it.isAnyLocalAddress }?.hostAddress
    }
}.getOrNull()

/** The phone's end. */
object MirrorRelayClient {
    private const val CONNECT_TIMEOUT_MS = 2_000
    /** Nothing for this long: the path to the PC may be dead (a sleeping Quest also sends nothing). Look again. */
    private const val READ_TIMEOUT_MS = 10_000
    private const val PROBE_TIMEOUT_MS = 2_000

    /** Connected and greeted. The PC closes a socket it refuses, which the first read then shows. */
    @Throws(IOException::class)
    fun connect(info: RelayInfo): Socket = open(info, RelayWire.hello(info.token), READ_TIMEOUT_MS)

    private fun open(info: RelayInfo, greeting: ByteArray, readTimeoutMs: Int): Socket {
        val socket = Socket()
        try {
            socket.connect(InetSocketAddress(info.host, info.port), CONNECT_TIMEOUT_MS)
            socket.tcpNoDelay = true
            socket.soTimeout = readTimeoutMs
            socket.getOutputStream().apply { write(greeting); flush() }
            return socket
        } catch (e: Exception) {
            runCatching { socket.close() }
            throw e
        }
    }

    /** Whether the PC can be reached from here, takes this token and has room. */
    fun reachable(info: RelayInfo): Boolean = try {
        open(info, RelayWire.probe(info.token), PROBE_TIMEOUT_MS).use { socket -> socket.getInputStream().read() >= 0 }
    } catch (_: IOException) {
        false
    }
}

/**
 * The PC's end: takes phones on [bindIp] and hands each one the packets of [onPacket]. [onPacket] never waits for the
 * network: every phone has its own bounded queue and writer thread, and a phone that falls behind loses frames up to the
 * next keyframe, without touching the PC's picture or the other phones.
 */
class MirrorRelayServer(bindIp: String, private val onClients: (Int) -> Unit = {}) : AutoCloseable {
    private val listener = ServerSocket(0, BACKLOG, InetAddress.getByName(bindIp))
    val info = RelayInfo(bindIp, listener.localPort, newToken())
    private val hello = RelayWire.hello(info.token)
    private val probe = RelayWire.probe(info.token)

    private val lock = Any()
    private val clients = ArrayList<Client>()
    private var session: ByteArray? = null
    private var config: ByteArray? = null
    /** Shown at once to a phone that joins, so that it has a picture before the next keyframe, or while nothing moves. */
    private var lastKeyFrame: ByteArray? = null
    @Volatile private var closed = false

    init {
        thread(name = "relay-accept", isDaemon = true) { acceptLoop() }
    }

    fun onPacket(packet: ScrcpyPacket) {
        synchronized(lock) {
            when {
                packet is ScrcpyPacket.Session -> {
                    val bytes = RelayWire.encode(packet)
                    session = bytes
                    config = null
                    lastKeyFrame = null
                    clients.forEach { it.offerControl(bytes) }
                }
                packet is ScrcpyPacket.Media && packet.config -> {
                    val bytes = RelayWire.encode(packet)
                    config = bytes
                    clients.forEach { it.offerControl(bytes) }
                }
                packet is ScrcpyPacket.Media && (packet.keyFrame || clients.isNotEmpty()) -> {
                    val bytes = RelayWire.encode(packet)
                    if (packet.keyFrame) lastKeyFrame = bytes
                    clients.forEach { it.offerFrame(bytes, packet.keyFrame) }
                }
            }
            clients.filter { it.stalled() }.forEach { it.shut() }
        }
    }

    /** Ends the relay and all its phones. Does not block. */
    override fun close() {
        closed = true
        runCatching { listener.close() }
        synchronized(lock) { clients.toList() }.forEach { it.shut() }
    }

    private fun acceptLoop() {
        while (!closed) {
            val socket = try {
                listener.accept()
            } catch (_: IOException) {
                return
            }
            try {
                val greeting = readGreeting(socket)
                when {
                    MessageDigest.isEqual(greeting, hello) -> add(socket)
                    MessageDigest.isEqual(greeting, probe) -> answerProbe(socket)
                    else -> socket.close()
                }
            } catch (_: IOException) {
                runCatching { socket.close() }
            }
        }
    }

    private fun readGreeting(socket: Socket): ByteArray {
        socket.soTimeout = HELLO_TIMEOUT_MS
        val input = socket.getInputStream()
        val received = ByteArrayOutputStream()
        while (received.size() < RelayWire.MAX_HELLO_BYTES) {
            val b = input.read()
            if (b < 0) break
            received.write(b)
            if (b == '\n'.code) break
        }
        return received.toByteArray()
    }

    private fun answerProbe(socket: Socket) = socket.use {
        val room = synchronized(lock) { !closed && clients.size < MAX_CLIENTS }
        if (room) socket.getOutputStream().write(1)
    }

    /** A phone that finds no room stays on its own stream. Taking the place of another would only move the problem around. */
    private fun add(socket: Socket) {
        socket.soTimeout = 0
        socket.tcpNoDelay = true
        socket.sendBufferSize = SEND_BUFFER_BYTES
        val client = Client(socket)
        val count = synchronized(lock) {
            if (closed || clients.size >= MAX_CLIENTS) {
                socket.close()
                return
            }
            client.offerControl(RelayWire.streamHeader())
            session?.let(client::offerControl)
            config?.let(client::offerControl)
            lastKeyFrame?.let(client::offerControl)
            clients.add(client)
            clients.size
        }
        client.start()
        onClients(count)
    }

    private fun remove(client: Client) {
        val count = synchronized(lock) {
            if (!clients.remove(client)) return
            clients.size
        }
        if (!closed) onClients(count)
    }

    private class Item(val bytes: ByteArray, val control: Boolean)

    private inner class Client(private val socket: Socket) {
        private val queueLock = Object()
        private val queue = ArrayDeque<Item>()
        private var queuedBytes = 0L
        private var waitingForKeyFrame = true
        private var shut = false
        @Volatile private var writingSinceNs = 0L

        /** Session and config packets always arrive; what follows them can only start at a keyframe. */
        fun offerControl(bytes: ByteArray) = synchronized(queueLock) {
            dropFrames()
            waitingForKeyFrame = true
            queue.addLast(Item(bytes, control = true))
            queuedBytes += bytes.size
            queueLock.notifyAll()
        }

        fun offerFrame(bytes: ByteArray, keyFrame: Boolean) = synchronized(queueLock) {
            if (queuedBytes + bytes.size > MAX_QUEUED_BYTES) {
                dropFrames()
                waitingForKeyFrame = true
            }
            if (waitingForKeyFrame && !keyFrame) return@synchronized
            waitingForKeyFrame = false
            queue.addLast(Item(bytes, control = false))
            queuedBytes += bytes.size
            queueLock.notifyAll()
        }

        fun stalled(): Boolean {
            val since = writingSinceNs
            return since != 0L && System.nanoTime() - since > WRITE_STALL_NS
        }

        fun start() {
            thread(name = "relay-write", isDaemon = true) { writeLoop() }
            // A phone sends nothing after its greeting: this only notices one that left, also while no video flows.
            thread(name = "relay-read", isDaemon = true) {
                try {
                    val input = socket.getInputStream()
                    while (input.read() >= 0) Unit
                } catch (_: IOException) {
                    // The same thing.
                }
                shut()
            }
        }

        /** Also frees a writer blocked in write(). */
        fun shut() {
            synchronized(queueLock) {
                shut = true
                queueLock.notifyAll()
            }
            runCatching { socket.close() }
        }

        private fun writeLoop() {
            try {
                val output = socket.getOutputStream()
                while (true) {
                    val item = synchronized(queueLock) {
                        while (queue.isEmpty() && !shut) queueLock.wait()
                        if (shut) return
                        queue.removeFirst().also { queuedBytes -= it.bytes.size }
                    }
                    writingSinceNs = System.nanoTime()
                    output.write(item.bytes)
                    writingSinceNs = 0L
                }
            } catch (_: IOException) {
                // The phone left.
            } finally {
                shut()
                remove(this)
            }
        }

        private fun dropFrames() {
            if (queue.removeAll { !it.control }) queuedBytes = queue.sumOf { it.bytes.size.toLong() }
        }
    }

    private companion object {
        const val BACKLOG = 4
        const val HELLO_TIMEOUT_MS = 3_000
        const val SEND_BUFFER_BYTES = 256 * 1024
        const val MAX_CLIENTS = 4
        /** About half a second of the PC's default 16 Mbps. */
        const val MAX_QUEUED_BYTES = 1024L * 1024
        const val WRITE_STALL_NS = 10_000_000_000L

        fun newToken(): String {
            val bytes = ByteArray(16).also { SecureRandom().nextBytes(it) }
            return bytes.joinToString("") { "%02x".format(it) }
        }
    }
}
