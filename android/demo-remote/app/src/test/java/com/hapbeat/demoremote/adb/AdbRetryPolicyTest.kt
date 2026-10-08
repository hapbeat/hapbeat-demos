package com.hapbeat.demoremote.adb

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class AdbRetryPolicyTest {
    @Test
    fun onlyNoAnswerIsRetried() {
        assertEquals(1_000L, AdbRetryPolicy.nextDelayMs(1, 3_000, AdbConnectResult.HostUnreachable, retryPortClosed = false))
        // Wi-Fi adb off: give up at once (USB re-enable is the fix).
        assertNull(AdbRetryPolicy.nextDelayMs(1, 100, AdbConnectResult.PortClosed, retryPortClosed = false))
        assertNull(AdbRetryPolicy.nextDelayMs(1, 100, AdbConnectResult.Connected, retryPortClosed = false))
        assertNull(AdbRetryPolicy.nextDelayMs(1, 100, AdbConnectResult.AuthTimeout, retryPortClosed = false))
        assertNull(AdbRetryPolicy.nextDelayMs(1, 100, AdbConnectResult.Rejected, retryPortClosed = false))
        assertNull(AdbRetryPolicy.nextDelayMs(1, 100, AdbConnectResult.Failed("x"), retryPortClosed = false))
    }

    @Test
    fun portClosedIsRetriedRightAfterTcpip() {
        assertEquals(1_000L, AdbRetryPolicy.nextDelayMs(1, 100, AdbConnectResult.PortClosed, retryPortClosed = true))
        assertNull(AdbRetryPolicy.nextDelayMs(1, 100, AdbConnectResult.Rejected, retryPortClosed = true))
    }

    @Test
    fun gapsGrowAndCap() {
        assertEquals(listOf(1_000L, 1_500L, 2_000L, 3_000L, 4_000L, 4_000L, 4_000L), (1..7).map { AdbRetryPolicy.retryDelayMs(it) })
        assertEquals(listOf(5_000L, 10_000L, 20_000L, 30_000L, 60_000L, 60_000L), (1..6).map { AdbRetryPolicy.reconnectDelayMs(it) })
    }

    @Test
    fun givesUpAfterAboutThirtySeconds() {
        assertEquals(4_000L, AdbRetryPolicy.nextDelayMs(6, 25_000, AdbConnectResult.HostUnreachable, retryPortClosed = false))
        assertNull(AdbRetryPolicy.nextDelayMs(6, 26_000, AdbConnectResult.HostUnreachable, retryPortClosed = false))
        // Tries that each time out after 5 s: the budget allows five of them, not the old fixed six.
        var elapsed = 0L
        var tries = 0
        while (true) {
            tries++
            elapsed += 5_000
            elapsed += AdbRetryPolicy.nextDelayMs(tries, elapsed, AdbConnectResult.HostUnreachable, retryPortClosed = false) ?: break
        }
        assertEquals(5, tries)
        assertTrue(elapsed <= AdbRetryPolicy.TOTAL_BUDGET_MS + 5_000)
    }

    @Test
    fun sleepHintAfterTwoUnansweredTries() {
        assertFalse(AdbRetryPolicy.showSleepHint(0))
        assertFalse(AdbRetryPolicy.showSleepHint(1))
        assertTrue(AdbRetryPolicy.showSleepHint(2))
    }

    @Test
    fun connectingText() {
        assertEquals("adb 接続中…（3 秒）", AdbRetryPolicy.connectingText(3, sleepSuspect = false))
        assertEquals("接続中（Quest の Wi-Fi が省電力中の可能性。12 秒）", AdbRetryPolicy.connectingText(12, sleepSuspect = true))
    }
}
