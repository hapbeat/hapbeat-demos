package com.hapbeat.demoremote.adb

import com.hapbeat.demoremote.DemoCatalog
import com.hapbeat.demoremote.net.DemoSwitchSocket
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class QuestAdbParseTest {
    @Test
    fun parsesShellOutputs() {
        assertEquals(57, QuestAdb.parseBatteryLevel("Current Battery Service state:\n  AC powered: false\n  level: 57\n  scale: 100\n"))
        assertNull(QuestAdb.parseBatteryLevel("nothing"))
        assertEquals(setOf("jp.hapbeat.demohub", "com.hapbeat.boxing"), QuestAdb.parsePackages("package:jp.hapbeat.demohub\r\npackage:com.hapbeat.boxing\n"))
        assertEquals("Quest 3S", QuestAdb.parseModel("Quest 3S\n"))
    }

    @Test
    fun launchCommandUsesFixedPackage() {
        assertEquals(
            "c=\$(cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER " +
                "jp.hapbeat.demohub | tail -n 1) && am start -n \"\$c\"",
            DemoCatalog.launchCommand(DemoCatalog.hub),
        )
    }

    @Test
    fun subnetBroadcast() {
        assertEquals("192.168.1.255", DemoSwitchSocket.subnetBroadcast(byteArrayOf(192.toByte(), 168.toByte(), 1, 23), 24))
        assertEquals("10.0.3.255", DemoSwitchSocket.subnetBroadcast(byteArrayOf(10, 0, 2, 5), 23))
        assertNull(DemoSwitchSocket.subnetBroadcast(byteArrayOf(10, 0, 2, 5), 32))
    }
}
