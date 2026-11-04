package com.hapbeat.demoremote.net

import com.hapbeat.demoremote.protocol.DemoSwitchProtocol
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.SocketException

/** This device's IPv4 address on the LAN and its prefix length. */
data class LocalIpv4(val address: InetAddress, val prefixLength: Int)

/** The LAN this device uses for Demo Switch and adb. */
interface PlatformNetwork {
    /** [onChanged] runs on any thread whenever the network comes, goes or changes. */
    fun start(onChanged: () -> Unit)
    fun stop()
    /** Null without a usable LAN (never a mobile network, where a broadcast or subnet scan would be meaningless). */
    fun localIpv4(): LocalIpv4?
    /** IPv4 default gateway of that LAN, or null. */
    fun gatewayIpv4(): String?
}

/**
 * One ephemeral UDP socket shared by DISCOVER / SWITCH / CONTROL. Kept open while the app is
 * in the foreground because READY arrives from the switched-to app on the same endpoint.
 */
class DemoSwitchSocket(
    private val scope: CoroutineScope,
    private val onPayload: (source: String, payload: ByteArray) -> Unit,
) {
    @Volatile private var socket: DatagramSocket? = null
    private var receiveJob: Job? = null

    val isOpen: Boolean get() = socket != null

    @Synchronized
    fun open() {
        if (socket != null) return
        val s = DatagramSocket(0).apply { broadcast = true }
        socket = s
        receiveJob = scope.launch(Dispatchers.IO) {
            // One byte larger than the limit so oversize datagrams are detectable.
            val buffer = ByteArray(DemoSwitchProtocol.MAX_PAYLOAD_BYTES + 1)
            while (isActive) {
                val packet = DatagramPacket(buffer, buffer.size)
                try {
                    s.receive(packet)
                } catch (_: SocketException) {
                    break // closed
                }
                if (packet.length > DemoSwitchProtocol.MAX_PAYLOAD_BYTES) continue
                val source = (packet.address as? Inet4Address)?.hostAddress ?: continue
                onPayload(source, buffer.copyOf(packet.length))
            }
        }
    }

    @Synchronized
    fun close() {
        socket?.close()
        socket = null
        receiveJob?.cancel()
        receiveJob = null
    }

    /** Returns false if the socket is closed or the send failed. */
    suspend fun send(address: String, payload: String): Boolean = withContext(Dispatchers.IO) {
        val s = socket ?: return@withContext false
        val bytes = payload.toByteArray(Charsets.UTF_8)
        if (bytes.size > DemoSwitchProtocol.MAX_PAYLOAD_BYTES) return@withContext false
        try {
            s.send(DatagramPacket(bytes, bytes.size, InetAddress.getByName(address), DemoSwitchProtocol.PORT))
            true
        } catch (_: java.io.IOException) {
            false
        }
    }

    companion object {
        /** True when the IPv4 [ip] is inside [network]/[prefixLength]. */
        fun inSubnet(ip: String, network: ByteArray, prefixLength: Int): Boolean {
            if (!DemoSwitchProtocol.isUnicastIpv4(ip) || network.size != 4 || prefixLength !in 0..32) return false
            val target = ip.split('.').fold(0L) { acc, part -> (acc shl 8) or part.toLong() }
            val base = network.fold(0L) { acc, b -> (acc shl 8) or (b.toLong() and 0xff) }
            val mask = if (prefixLength == 0) 0L else (0xffffffffL shl (32 - prefixLength)) and 0xffffffffL
            return (target and mask) == (base and mask)
        }

        fun subnetBroadcast(address: ByteArray, prefixLength: Int): String? {
            if (address.size != 4 || prefixLength !in 1..30) return null
            val ip = address.fold(0L) { acc, b -> (acc shl 8) or (b.toLong() and 0xff) }
            val hostMask = (1L shl (32 - prefixLength)) - 1
            val b = ip or hostMask
            return "${(b shr 24) and 0xff}.${(b shr 16) and 0xff}.${(b shr 8) and 0xff}.${b and 0xff}"
        }

        const val LIMITED_BROADCAST = "255.255.255.255"
    }
}
