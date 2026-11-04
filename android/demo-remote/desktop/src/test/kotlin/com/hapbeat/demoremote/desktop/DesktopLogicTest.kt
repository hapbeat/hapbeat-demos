package com.hapbeat.demoremote.desktop

import com.hapbeat.demoremote.data.MirrorSettings
import com.hapbeat.demoremote.data.SettingsStore
import com.hapbeat.demoremote.mirror.MirrorLayout
import com.hapbeat.demoremote.mirror.MirrorPlacement
import com.hapbeat.demoremote.net.DefaultRoute
import com.hapbeat.demoremote.net.InterfaceCandidate
import com.hapbeat.demoremote.net.InterfacePick
import com.hapbeat.demoremote.ui.Monitor
import com.hapbeat.demoremote.ui.MonitorChoice
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.nio.file.Files

class DesktopLogicTest {
    private fun tempDir(): File = Files.createTempDirectory("desktop-test").toFile()

    @Test
    fun propertiesStoreRoundTripsAndLeavesNoTempFile() {
        val dir = tempDir()
        val file = File(dir, "nested/settings.properties")
        val store = PropertiesStore(file)
        assertTrue(store.write(durable = true) {
            putString("s", "日本語 =:#")
            putInt("i", 42)
            putLong("l", 9_007_199_254_740_991)
            putBoolean("b", true)
            putString("gone", "x")
        })
        assertTrue(store.write { remove("gone") })
        val again = PropertiesStore(file)
        assertEquals("日本語 =:#", again.getString("s"))
        assertEquals(42, again.getInt("i", 0))
        assertEquals(9_007_199_254_740_991, again.getLong("l", 0))
        assertTrue(again.getBoolean("b", false))
        assertNull(again.getString("gone"))
        assertEquals(7, again.getInt("missing", 7))
        assertEquals(listOf("settings.properties"), file.parentFile.list()!!.toList())
    }

    @Test
    fun settingsStoreOnPropertiesKeepsTheSequenceAndUsesThePrefix() {
        val file = File(tempDir(), "settings.properties")
        val settings = SettingsStore(PropertiesStore(file), "pc-", MirrorSettings(maxSize = 1920, bitRate = 16_000_000))
        // The controller_id rule of specs/demo-switch-control.md.
        assertTrue(settings.controllerId, Regex("[a-z0-9][a-z0-9._-]{0,63}").matches(settings.controllerId))
        assertTrue(settings.controllerId.startsWith("pc-"))
        assertEquals(1, settings.readNext())
        assertTrue(settings.writeNextSync(11))
        val again = SettingsStore(PropertiesStore(file), "pc-", MirrorSettings())
        assertEquals(settings.controllerId, again.controllerId)
        assertEquals(11, again.readNext())
        assertEquals(1024, again.mirrorSettings.maxSize) // the defaults given; nothing was saved
    }

    @Test
    fun interfacePickPrefersTheDefaultRouteOnARealAdapter() {
        val wifi = InterfaceCandidate("wlan0", "Intel Wi-Fi 6", "192.168.0.10", 24)
        val ethernet = InterfaceCandidate("eth0", "Realtek PCIe GbE", "192.168.1.5", 24)
        val wsl = InterfaceCandidate("eth3", "Hyper-V Virtual Ethernet Adapter", "172.25.0.1", 20)
        val vethernet = InterfaceCandidate("eth4", "vEthernet (WSL)", "172.26.0.1", 20)
        val loop = InterfaceCandidate("lo", "Software Loopback Interface 1", "127.0.0.1", 8, loopback = true)
        val public = InterfaceCandidate("eth5", "Some Adapter", "8.8.8.8", 24, siteLocal = false)
        val down = InterfaceCandidate("eth6", "Down Adapter", "192.168.5.5", 24, up = false)
        val all = listOf(wsl, vethernet, loop, public, down, ethernet, wifi)
        assertEquals(wifi, InterfacePick.pick(all, "192.168.0.10"))
        assertEquals(ethernet, InterfacePick.pick(all, null))
        // A default route over a VPN-like adapter does not make it the LAN.
        assertEquals(ethernet, InterfacePick.pick(all, "172.26.0.1"))
        assertNull(InterfacePick.pick(listOf(wsl, loop, public), null))
    }

    @Test
    fun defaultRoutesParseLowestMetricFirst() {
        val output = """
            IPv4 Route Table
            ===========================================================================
            Active Routes:
            Network Destination        Netmask          Gateway       Interface  Metric
                      0.0.0.0          0.0.0.0     192.168.1.1     192.168.1.5     35
                      0.0.0.0          0.0.0.0     192.168.0.1    192.168.0.10     25
                      0.0.0.0          0.0.0.0         On-link        10.8.0.2      5
            ===========================================================================
        """.trimIndent().replace("\n", "\r\n")
        assertEquals(
            listOf(DefaultRoute(null, "10.8.0.2"), DefaultRoute("192.168.0.1", "192.168.0.10"), DefaultRoute("192.168.1.1", "192.168.1.5")),
            InterfacePick.parseDefaultRoutes(output),
        )
    }

    @Test
    fun monitorChoiceKeepsTheSavedOneElseTheFirstNonPrimary() {
        val primary = Monitor("\\Display0", "ディスプレイ 1（2560×1440）", primary = true)
        val second = Monitor("\\Display1", "ディスプレイ 2（1920×1080）", primary = false)
        val third = Monitor("\\Display2", "ディスプレイ 3（1920×1080）", primary = false)
        assertEquals(third, MonitorChoice.resolve(listOf(primary, second, third), "\\Display2"))
        assertEquals(primary, MonitorChoice.resolve(listOf(primary, second), "\\Display0"))
        assertEquals(second, MonitorChoice.resolve(listOf(primary, second), "\\Display9"))
        assertEquals(second, MonitorChoice.resolve(listOf(primary, second), null))
        assertEquals(primary, MonitorChoice.resolve(listOf(primary), null))
        assertNull(MonitorChoice.resolve(emptyList(), null))
        assertEquals("ディスプレイ 2（1920×1080）", MonitorChoice.label(1, 1920, 1080))
    }

    @Test
    fun mirrorLayoutFitsWithBarsOrFillsWithACentreCrop() {
        // The left eye (nearly square) on a 16:9 projector.
        assertEquals(MirrorPlacement(0, 0, 1832, 1920, 44 + 400 + 16, 0, 1030, 1080).copy(dstX = (1920 - 1030) / 2), MirrorLayout.place(1832, 1920, 1920, 1080, fill = false))
        assertEquals(MirrorPlacement(0, 445, 1832, 1030, 0, 0, 1920, 1080), MirrorLayout.place(1832, 1920, 1920, 1080, fill = true))
        // A wide frame in a tall view.
        assertEquals(MirrorPlacement(0, 0, 1920, 1080, 0, 381, 800, 450).copy(dstY = (1212 - 450) / 2), MirrorLayout.place(1920, 1080, 800, 1212, fill = false))
        val filled = MirrorLayout.place(1920, 1080, 800, 1200, fill = true)
        assertEquals(720, filled.srcWidth)
        assertEquals(600, filled.srcX)
        assertFalse(filled.srcHeight != 1080)
    }
}
