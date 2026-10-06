package com.hapbeat.demoremote.adb

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.io.IOException
import java.net.ConnectException
import java.net.NoRouteToHostException
import java.net.SocketTimeoutException

class AdbConnectClassifyTest {
    @Test
    fun refusedMeansPortClosed() {
        assertEquals(AdbConnectResult.PortClosed, QuestAdb.classifyTcpFailure(ConnectException("Connection refused")))
        assertEquals(
            AdbConnectResult.PortClosed,
            QuestAdb.classifyTcpFailure(
                ConnectException("failed to connect to /192.168.0.5 (port 5555) after 3000ms: isConnected failed: ECONNREFUSED (Connection refused)"),
            ),
        )
        assertEquals(AdbConnectResult.PortClosed, QuestAdb.classifyTcpFailure(ConnectException()))
    }

    @Test
    fun noAnswerMeansHostUnreachable() {
        assertEquals(AdbConnectResult.HostUnreachable, QuestAdb.classifyTcpFailure(SocketTimeoutException("connect timed out")))
        assertEquals(AdbConnectResult.HostUnreachable, QuestAdb.classifyTcpFailure(NoRouteToHostException("Host unreachable")))
        assertEquals(AdbConnectResult.HostUnreachable, QuestAdb.classifyTcpFailure(IOException("boom")))
        assertEquals(
            AdbConnectResult.HostUnreachable,
            QuestAdb.classifyTcpFailure(ConnectException("failed to connect: connect failed: ENETUNREACH (Network is unreachable)")),
        )
    }

    @Test
    fun questStateLine() {
        assertEquals(12345L, QuestAdb.parseUptimeSeconds("12345.67 23456.78\n"))
        assertNull(QuestAdb.parseUptimeSeconds("cat: /proc/uptime: Permission denied"))
        assertEquals(
            "開発者 1・adb 1・起動から 3時間25分・UP1A.231005",
            QuestAdb.describeState("1\n", "1\n", "UP1A.231005\n", 3L * 3600 + 25 * 60 + 9),
        )
        assertEquals("開発者 ?・adb 0・起動時間 不明・build 不明", QuestAdb.describeState("", "0", "", null))
    }
}
