package com.hapbeat.demoremote.desktop

import com.hapbeat.demoremote.adb.AdbDeviceLine
import com.hapbeat.demoremote.adb.AdbOutput
import com.hapbeat.demoremote.adb.UsbAdbException
import com.hapbeat.demoremote.ui.QuestApkCatalog
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant

class InstallLogicTest {
    @Test
    fun devicesParseWithStateAndModel() {
        val output = """
            List of devices attached
            2G0YC1ZF7X0BJW         device usb:1-2 product:panther model:Quest_3S device:panther transport_id:1
            192.168.0.37:5555      device product:panther model:Quest_3S device:panther transport_id:2
            R5CT10XYZ              unauthorized usb:1-3 transport_id:3

        """.trimIndent().replace("\n", "\r\n")
        assertEquals(
            listOf(
                AdbDeviceLine("2G0YC1ZF7X0BJW", "device", "Quest_3S"),
                AdbDeviceLine("192.168.0.37:5555", "device", "Quest_3S"),
                AdbDeviceLine("R5CT10XYZ", "unauthorized", ""),
            ),
            AdbOutput.parseDevices(output),
        )
    }

    @Test
    fun theQuestOnTheCableIsPickedAndAPhoneIsNot() {
        val wifi = AdbDeviceLine("192.168.0.37:5555", "device", "Quest_3S")
        val mdns = AdbDeviceLine("adb-2G0Y-abc._adb-tls-connect._tcp.", "device", "Quest_3")
        val phone = AdbDeviceLine("R5CT10XYZ", "device", "Pixel_8")
        val usbQuest = AdbDeviceLine("2G0YC1ZF7X0BJW", "device", "Quest_3S")
        val unauthorized = AdbDeviceLine("2G0Y00000", "unauthorized", "")
        assertEquals(usbQuest, AdbOutput.pickUsbQuest(listOf(wifi, mdns, phone, unauthorized, usbQuest)))
        assertTrue(assertThrows(UsbAdbException::class.java) { AdbOutput.pickUsbQuest(listOf(wifi, phone, unauthorized)) }.message!!.contains("許可"))
        assertTrue(assertThrows(UsbAdbException::class.java) { AdbOutput.pickUsbQuest(listOf(wifi, mdns, phone)) }.message!!.contains("ありません"))
    }

    @Test
    fun lastUpdateTimeIsReadInTheQuestsOwnZone() {
        val output = "+0900\r\n    lastUpdateTime=2026-10-20 10:11:12\r\n"
        assertEquals(Instant.parse("2026-10-20T01:11:12Z").toEpochMilli(), QuestApkCatalog.parseLastUpdate(output))
        assertEquals(Instant.parse("2026-10-20T10:11:12Z").toEpochMilli(), QuestApkCatalog.parseLastUpdate("+0000\n  lastUpdateTime=2026-10-20 10:11:12"))
        assertNull("not installed", QuestApkCatalog.parseLastUpdate("+0900\n"))
        assertNull(QuestApkCatalog.parseLastUpdate(""))
    }

    @Test
    fun newerAndVerifiedRules() {
        assertTrue(QuestApkCatalog.isNewer(100, null))
        assertTrue(QuestApkCatalog.isNewer(100, 50))
        assertFalse(QuestApkCatalog.isNewer(50, 100))
        assertFalse(QuestApkCatalog.isNewer(null, 100))
        assertTrue(QuestApkCatalog.installVerified(null, 100))
        assertTrue(QuestApkCatalog.installVerified(50, 100))
        assertFalse(QuestApkCatalog.installVerified(100, 100))
        assertFalse(QuestApkCatalog.installVerified(100, null))
    }

    @Test
    fun theTableMatchesInstallDemos() {
        // Same names as tools/install-demos.ps1 (package and file in quest-apks/).
        val byFile = QuestApkCatalog.apks.associate { it.fileName to it.packageName }
        assertEquals(9, byFile.size)
        assertEquals("jp.hapbeat.demohub", byFile["DemoHub.apk"])
        assertEquals("com.Hapbeat.HapticHandDemo_G2", byFile["HandDemo.apk"])
    }
}
