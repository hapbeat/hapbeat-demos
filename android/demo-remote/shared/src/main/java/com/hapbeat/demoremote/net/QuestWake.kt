package com.hapbeat.demoremote.net

import com.hapbeat.demoremote.adb.AdbRetryPolicy
import com.hapbeat.demoremote.protocol.DemoSwitchProtocol
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress

/**
 * Wakes a Quest whose Wi-Fi is in power save before an adb connect: a few small UDP datagrams to its Demo Switch port
 * (a unicast DISCOVER [payload]) get ARP resolved and the headset's radio listening, so the TCP SYN that follows is
 * answered sooner. Sent from a throwaway socket, so a HERE that may come back is never read. Best effort: failures are
 * ignored, the adb connect reports the real outcome.
 */
object QuestWake {
    suspend fun wake(ip: String, payload: ByteArray) = withContext(Dispatchers.IO) {
        try {
            DatagramSocket().use { s ->
                val packet = packetTo(ip, payload)
                repeat(AdbRetryPolicy.WAKE_PACKETS) { i ->
                    if (i > 0) delay(AdbRetryPolicy.WAKE_INTERVAL_MS)
                    s.send(packet)
                }
            }
        } catch (_: IOException) {
            // Best effort (see class doc).
        }
    }

    /** [wake] for a plain thread (the mirror connects on its own thread). */
    fun wakeBlocking(ip: String, payload: ByteArray) {
        try {
            DatagramSocket().use { s ->
                val packet = packetTo(ip, payload)
                repeat(AdbRetryPolicy.WAKE_PACKETS) { i ->
                    if (i > 0) Thread.sleep(AdbRetryPolicy.WAKE_INTERVAL_MS)
                    s.send(packet)
                }
            }
        } catch (_: IOException) {
            // Best effort (see class doc).
        }
    }

    private fun packetTo(ip: String, payload: ByteArray) =
        DatagramPacket(payload, payload.size, InetAddress.getByName(ip), DemoSwitchProtocol.PORT)
}
