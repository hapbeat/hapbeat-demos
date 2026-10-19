package com.hapbeat.demoremote.adb

import com.hapbeat.demoremote.DemoCatalog
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class DeviceAddressFileTest {
    private val unset = DeviceAddressFile.UNSET

    @Test
    fun validatesAxes() {
        assertTrue(DeviceAddressFile.isValid(-1))
        assertTrue(DeviceAddressFile.isValid(1))
        assertTrue(DeviceAddressFile.isValid(99))
        assertFalse(DeviceAddressFile.isValid(0))
        assertFalse(DeviceAddressFile.isValid(100))
        assertFalse(DeviceAddressFile.isValid(-2))
    }

    @Test
    fun buildsContentLikeInstallDemos() {
        // install-demos.ps1: {"version":1,"player":$Player,"group":$Group} and a newline.
        assertEquals("{\"version\":1,\"player\":-1,\"group\":2}\n", DeviceAddressFile.content(DeviceAddress(unset, 2)))
        assertEquals("{\"version\":1,\"player\":99,\"group\":1}\n", DeviceAddressFile.content(DeviceAddress(99, 1)))
        assertThrows(IllegalArgumentException::class.java) { DeviceAddressFile.content(DeviceAddress(0, 1)) }
        assertThrows(IllegalArgumentException::class.java) { DeviceAddressFile.content(DeviceAddress(1, 100)) }
    }

    @Test
    fun commands() {
        val pkg = DemoCatalog.HUB_PACKAGE
        assertEquals("/sdcard/Android/data/jp.hapbeat.demohub/files/hapbeat-device.json", DeviceAddressFile.path(pkg))
        assertEquals("mkdir -p /sdcard/Android/data/jp.hapbeat.demohub/files", DeviceAddressFile.mkdirCommand(pkg))
        assertEquals(
            "printf '%s\\n' '{\"version\":1,\"player\":-1,\"group\":3}' > /sdcard/Android/data/jp.hapbeat.demohub/files/hapbeat-device.json",
            DeviceAddressFile.writeCommand(pkg, DeviceAddress(unset, 3)),
        )
        assertEquals("cat /sdcard/Android/data/jp.hapbeat.demohub/files/hapbeat-device.json", DeviceAddressFile.readCommand(pkg))
    }

    @Test
    fun parsesCat() {
        assertEquals(
            DeviceAddressRead.Value(DeviceAddress(unset, 2)),
            DeviceAddressFile.parse(0, "{\"version\":1,\"player\":-1,\"group\":2}\n"),
        )
        // Whitespace and key order do not matter.
        assertEquals(
            DeviceAddressRead.Value(DeviceAddress(5, 7)),
            DeviceAddressFile.parse(0, "{ \"group\": 7, \"version\": 1, \"player\": 5 }\r\n"),
        )
        assertEquals(
            DeviceAddressRead.Missing,
            DeviceAddressFile.parse(1, "cat: /sdcard/Android/data/x/files/hapbeat-device.json: No such file or directory\n"),
        )
        assertEquals(DeviceAddressRead.Invalid, DeviceAddressFile.parse(1, "cat: ...: Permission denied"))
        listOf(
            "",
            "not json",
            "{\"version\":2,\"player\":1,\"group\":1}",
            "{\"version\":1,\"player\":0,\"group\":1}",
            "{\"version\":1,\"player\":1,\"group\":100}",
            "{\"version\":1,\"player\":1}",
            "{\"version\":1,\"player\":1,\"group\":1,\"extra\":1}",
            "{\"version\":1,\"player\":1.5,\"group\":1}",
            "{\"version\":1,\"player\":\"1\",\"group\":1}",
            "{\"version\":1,\"player\":1,\"group\":1,\"group\":2}",
            "[1,2]",
        ).forEach { assertEquals(it, DeviceAddressRead.Invalid, DeviceAddressFile.parse(0, it)) }
    }

    @Test
    fun verifiesReadBack() {
        val written = DeviceAddress(1, 2)
        assertTrue(DeviceAddressFile.verified(written, DeviceAddressRead.Value(DeviceAddress(1, 2))))
        assertFalse(DeviceAddressFile.verified(written, DeviceAddressRead.Value(DeviceAddress(1, 3))))
        assertFalse(DeviceAddressFile.verified(written, DeviceAddressRead.Missing))
        assertFalse(DeviceAddressFile.verified(written, DeviceAddressRead.Invalid))
    }

    @Test
    fun targetsAreInstalledKnownDemos() {
        val installed = setOf("com.hapbeat.boxing", DemoCatalog.HUB_PACKAGE, "com.other.app")
        // Catalog order: the Hub first.
        assertEquals(listOf(DemoCatalog.HUB_PACKAGE, "com.hapbeat.boxing"), DeviceAddressFile.targets(installed).map { it.packageName })
        assertTrue(DeviceAddressFile.targets(setOf("com.other.app")).isEmpty())
    }

    @Test
    fun summarizesHubFirst() {
        val hub = DemoCatalog.HUB_PACKAGE
        val two = DeviceAddressRead.Value(DeviceAddress(unset, 2))
        assertNull(DeviceAddressFile.summarize(emptyList()))
        assertEquals(
            DeviceAddressSummary(hub, two, mixed = false),
            DeviceAddressFile.summarize(listOf("com.hapbeat.boxing" to two, hub to two)),
        )
        // No Hub: the first demo found.
        assertEquals(
            DeviceAddressSummary("com.hapbeat.boxing", two, mixed = true),
            DeviceAddressFile.summarize(listOf("com.hapbeat.boxing" to two, "jp.hapbeat.volley" to DeviceAddressRead.Missing)),
        )
        // A missing file is the same as -1 / -1.
        assertEquals(
            false,
            DeviceAddressFile.summarize(listOf(hub to DeviceAddressRead.Missing, "x" to DeviceAddressRead.Value(DeviceAddress(unset, unset))))?.mixed,
        )
        // An invalid file differs from every value.
        assertEquals(true, DeviceAddressFile.summarize(listOf(hub to two, "x" to DeviceAddressRead.Invalid))?.mixed)
        assertEquals(false, DeviceAddressFile.summarize(listOf(hub to DeviceAddressRead.Invalid))?.mixed)
    }

    @Test
    fun hubStaleness() {
        val hub = DemoCatalog.HUB_PACKAGE
        val file = DeviceAddressSummary(hub, DeviceAddressRead.Value(DeviceAddress(unset, 2)), mixed = false)
        assertTrue(DeviceAddressFile.hubIsStale(file, DeviceAddress(unset, 1)))
        assertFalse(DeviceAddressFile.hubIsStale(file, DeviceAddress(unset, 2)))
        assertFalse(DeviceAddressFile.hubIsStale(file, null))
        assertFalse(DeviceAddressFile.hubIsStale(null, DeviceAddress(unset, 1)))
        // Not the Hub's file: says nothing about the Hub.
        assertFalse(DeviceAddressFile.hubIsStale(file.copy(primaryPackage = "com.hapbeat.boxing"), DeviceAddress(unset, 1)))
        assertFalse(DeviceAddressFile.hubIsStale(file.copy(primary = DeviceAddressRead.Invalid), DeviceAddress(unset, 1)))
        // No file: the Hub applies nothing, which reads back as -1 / -1.
        assertFalse(DeviceAddressFile.hubIsStale(file.copy(primary = DeviceAddressRead.Missing), DeviceAddress(unset, unset)))
    }

    @Test
    fun steps() {
        assertEquals(1, DeviceAddressFile.step(unset, 1))
        assertEquals(unset, DeviceAddressFile.step(unset, -1))
        assertEquals(unset, DeviceAddressFile.step(1, -1))
        assertEquals(3, DeviceAddressFile.step(2, 1))
        assertEquals(99, DeviceAddressFile.step(99, 1))
        assertEquals(98, DeviceAddressFile.step(99, -1))
    }

    @Test
    fun texts() {
        assertEquals("指定なし", DeviceAddressFile.axisText(unset))
        assertEquals("12", DeviceAddressFile.axisText(12))
        assertEquals("書けた 8 本", DeviceAddressFile.resultText(8, emptyList()))
        assertEquals("書けた 6 本 / 失敗 2 本（Boxing、FPS）", DeviceAddressFile.resultText(6, listOf("Boxing", "FPS")))
        val hub = DemoCatalog.HUB_PACKAGE
        assertEquals("グループ --", DeviceAddressFile.groupStatus(null))
        assertEquals("グループ 2", DeviceAddressFile.groupStatus(DeviceAddressSummary(hub, DeviceAddressRead.Value(DeviceAddress(1, 2)), false)))
        assertEquals("グループ 指定なし", DeviceAddressFile.groupStatus(DeviceAddressSummary(hub, DeviceAddressRead.Missing, false)))
        assertEquals("グループ 混在", DeviceAddressFile.groupStatus(DeviceAddressSummary(hub, DeviceAddressRead.Missing, true)))
        assertEquals("グループ --", DeviceAddressFile.groupStatus(DeviceAddressSummary(hub, DeviceAddressRead.Invalid, false)))
    }
}
