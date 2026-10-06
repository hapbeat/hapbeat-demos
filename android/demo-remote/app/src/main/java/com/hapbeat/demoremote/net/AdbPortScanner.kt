package com.hapbeat.demoremote.net

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import kotlinx.coroutines.withContext
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket

/**
 * Finds hosts with Wi-Fi adb (TCP 5555) on the local subnet, so a Quest sitting on its home screen
 * (no Demo Switch receiver to answer DISCOVER) still shows up for "Hub を開く" and the mirror.
 * Only a TCP connect is made; no adb handshake, so no USB-debugging dialog appears in the headset.
 */
object AdbPortScanner {
    const val PORT = 5555
    const val TIMEOUT_MS = 400
    /** Second try for a saved Quest whose port looked closed: a slow or just-woken headset. */
    const val SAVED_RETRY_TIMEOUT_MS = 1500
    private const val PARALLEL = 64

    /** Host addresses of [address]/[prefixLength] except network, broadcast and [address] itself; empty above /22. */
    fun subnetHosts(address: ByteArray, prefixLength: Int): List<String> {
        if (address.size != 4 || prefixLength !in 22..30) return emptyList()
        val ip = address.fold(0L) { acc, b -> (acc shl 8) or (b.toLong() and 0xff) }
        val hostMask = (1L shl (32 - prefixLength)) - 1
        val network = ip and hostMask.inv() and 0xffffffffL
        return (1 until hostMask).map { network + it }.filter { it != ip }.map {
            "${(it shr 24) and 0xff}.${(it shr 16) and 0xff}.${(it shr 8) and 0xff}.${it and 0xff}"
        }
    }

    suspend fun scan(hosts: List<String>, timeoutMs: Int = TIMEOUT_MS): List<String> = withContext(Dispatchers.IO) {
        val permits = Semaphore(PARALLEL)
        coroutineScope {
            hosts.map { host ->
                async {
                    permits.withPermit {
                        try {
                            Socket().use { it.connect(InetSocketAddress(host, PORT), timeoutMs) }
                            host
                        } catch (_: IOException) {
                            null
                        }
                    }
                }
            }.awaitAll().filterNotNull()
        }
    }
}
