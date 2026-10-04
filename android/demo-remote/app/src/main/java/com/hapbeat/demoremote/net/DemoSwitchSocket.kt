package com.hapbeat.demoremote.net

import android.content.Context
import android.net.ConnectivityManager
import android.net.LinkAddress
import android.net.Network
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
        /** Subnet broadcast of [network]'s IPv4 address (Wi-Fi when bound); 255.255.255.255 if unknown. */
        fun broadcastAddress(context: Context, network: Network?): String {
            val la = localIpv4(context, network) ?: return LIMITED_BROADCAST
            return subnetBroadcast(la.address.address, la.prefixLength) ?: LIMITED_BROADCAST
        }

        /** This device's IPv4 address and prefix on [network] (Wi-Fi when bound). */
        fun localIpv4(context: Context, network: Network?): LinkAddress? {
            val cm = context.getSystemService(ConnectivityManager::class.java) ?: return null
            val link = cm.getLinkProperties(network ?: cm.activeNetwork) ?: return null
            return link.linkAddresses.firstOrNull { it.address is Inet4Address }
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
