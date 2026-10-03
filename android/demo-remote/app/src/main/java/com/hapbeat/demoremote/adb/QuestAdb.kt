package com.hapbeat.demoremote.adb

import android.content.Context
import dadb.AdbAuthException
import dadb.AdbKeyPair
import dadb.AdbShellResponse
import dadb.Dadb
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.async
import kotlinx.coroutines.cancel
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import java.io.File
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket

object AdbKeys {
    /** Loads (or generates once) this app's RSA key pair under filesDir. minSdk 26 allows dadb's own helpers. */
    fun load(context: Context): AdbKeyPair {
        val priv = File(context.filesDir, "adbkey")
        val pub = File(context.filesDir, "adbkey.pub")
        if (!priv.isFile || !pub.isFile) AdbKeyPair.generate(priv, pub)
        return AdbKeyPair.read(priv, pub)
    }
}

sealed interface AdbConnectResult {
    data object Connected : AdbConnectResult
    /** TCP 5555 refused / timed out: Wi-Fi adb is off (e.g. after a Quest reboot). */
    data object Unreachable : AdbConnectResult
    /** No answer to the USB debugging dialog within the wait time (or cancelled). */
    data object AuthTimeout : AdbConnectResult
    data object Rejected : AdbConnectResult
    data class Failed(val reason: String) : AdbConnectResult
}

/**
 * One dadb connection per Quest, shared by shell commands and the scrcpy stream
 * (separate ADB streams on the same transport).
 */
class QuestAdb(val ip: String, private val keyPair: AdbKeyPair) {
    @Volatile var dadb: Dadb? = null
        private set

    /**
     * Connects to [ip]:5555. The first connection shows the USB debugging dialog in the
     * headset; dadb blocks until it is answered, so the handshake runs on a detached IO job
     * and [onAuthWaiting] is called when it has not finished after a short delay.
     */
    suspend fun connect(onAuthWaiting: () -> Unit): AdbConnectResult {
        close()
        val reachable = withContext(Dispatchers.IO) {
            try {
                Socket().use { it.connect(InetSocketAddress(ip, PORT), CONNECT_TIMEOUT_MS) }
                true
            } catch (_: IOException) {
                false
            }
        }
        if (!reachable) return AdbConnectResult.Unreachable

        val candidate = Dadb.create(ip, PORT, keyPair, CONNECT_TIMEOUT_MS, 0)
        // Detached: a blocked handshake read cannot be interrupted; dadb only exposes its socket
        // after the handshake, so on timeout the thread stays parked until the headset answers
        // or drops the TCP connection.
        val handshakeScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
        val handshake = handshakeScope.async { candidate.shell("echo ok") }
        try {
            // join() waits for completion without rethrowing the handshake's own failure.
            var done = withTimeoutOrNull(AUTH_HINT_DELAY_MS) { handshake.join(); true } ?: false
            if (!done) {
                onAuthWaiting()
                done = withTimeoutOrNull(AUTH_WAIT_MS - AUTH_HINT_DELAY_MS) { handshake.join(); true } ?: false
            }
            if (!done) {
                candidate.close()
                handshakeScope.cancel()
                return AdbConnectResult.AuthTimeout
            }
            val error = runCatching { handshake.await() }.exceptionOrNull()
            if (error != null) {
                candidate.close()
                return when {
                    error is AdbAuthException -> AdbConnectResult.Rejected
                    error.cause is java.net.ConnectException || error is java.net.ConnectException -> AdbConnectResult.Unreachable
                    else -> AdbConnectResult.Failed(error.javaClass.simpleName)
                }
            }
            dadb = candidate
            return AdbConnectResult.Connected
        } catch (e: kotlinx.coroutines.CancellationException) {
            candidate.close()
            handshakeScope.cancel()
            throw e
        }
    }

    /** Runs a shell command; any I/O failure means the connection is gone (caller marks it disconnected). */
    suspend fun shell(command: String): AdbShellResponse = withContext(Dispatchers.IO) {
        val d = dadb ?: throw IOException("adb not connected")
        d.shell(command)
    }

    fun close() {
        val d = dadb
        dadb = null
        try {
            d?.close()
        } catch (_: Exception) {
            // Closing a broken transport may throw; the connection is discarded either way.
        }
    }

    companion object {
        const val PORT = 5555
        const val CONNECT_TIMEOUT_MS = 3000
        const val AUTH_WAIT_MS = 30_000L
        const val AUTH_HINT_DELAY_MS = 1500L

        fun parseModel(output: String): String = output.trim()

        /** `dumpsys battery` -> level (0..100) or null. */
        fun parseBatteryLevel(output: String): Int? =
            output.lineSequence().map { it.trim() }.firstOrNull { it.startsWith("level:") }
                ?.removePrefix("level:")?.trim()?.toIntOrNull()

        /** `pm list packages` -> package names. */
        fun parsePackages(output: String): Set<String> =
            output.lineSequence().map { it.trim() }.filter { it.startsWith("package:") }.map { it.removePrefix("package:") }.toSet()
    }
}
