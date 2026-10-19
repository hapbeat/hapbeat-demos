package com.hapbeat.demoremote.data

import com.hapbeat.demoremote.DemoCatalog
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * demo-session.md「デモごとの設定」: the phone's copy of each Quest's per-demo settings, with the changes saved while the Hub
 * was not in front kept as unwritten until written; and the per-demo (`scope: device`) options a preset never holds.
 */
class DemoSettingsStoreTest {
    private val quest = DemoSettingsStore.questKeys("2G0YC5ZG8N01AB", "192.168.1.20")
    private val other = DemoSettingsStore.questKeys("", "192.168.1.21")

    @Test
    fun saveWithoutTheHubKeepsTheChangeUnwrittenAcrossARestart() {
        var book = DemoSettingsStore.onRead(emptyMap(), quest, "boxing", mapOf("round" to "55", "haptics_db" to "2"))
        book = DemoSettingsStore.save(book, quest, "boxing", mapOf("round" to "55", "haptics_db" to "6"))
        val entry = DemoSettingsStore.entry(book, quest, "boxing")!!
        assertEquals("Only what differs from the Hub.", mapOf("haptics_db" to "6"), entry.unwritten)
        assertEquals(mapOf("round" to "55", "haptics_db" to "6"), entry.values)

        // Closing the app: the settings string is read back.
        val restored = DemoSettingsStore.decode(DemoSettingsStore.encode(book))
        assertEquals(book, restored)
        assertEquals(listOf(UnwrittenDemoSettings("serial:2G0YC5ZG8N01AB", "boxing", mapOf("haptics_db" to "6"))),
            DemoSettingsStore.unwritten(restored, quest))
        assertTrue("Another Quest has its own settings.", DemoSettingsStore.unwritten(restored, other).isEmpty())
        assertNull(DemoSettingsStore.entry(restored, other, "boxing"))
    }

    @Test
    fun writingOrReadingTheSameValueClearsTheUnwrittenChange() {
        var book = DemoSettingsStore.save(emptyMap(), quest, "boxing", mapOf("haptics_db" to "6", "round" to "30"))
        assertEquals("Hub values unknown: everything is unwritten.", mapOf("haptics_db" to "6", "round" to "30"),
            DemoSettingsStore.entry(book, quest, "boxing")!!.unwritten)
        book = DemoSettingsStore.onWritten(book, quest, "boxing", mapOf("haptics_db" to "6"))
        assertEquals(mapOf("round" to "30"), DemoSettingsStore.entry(book, quest, "boxing")!!.unwritten)
        assertEquals("6", DemoSettingsStore.entry(book, quest, "boxing")!!.hub["haptics_db"])
        // The Hub already has round 30 (written from elsewhere): reading clears it.
        book = DemoSettingsStore.onRead(book, quest, "boxing", mapOf("round" to "30", "haptics_db" to "6"))
        assertTrue(DemoSettingsStore.entry(book, quest, "boxing")!!.unwritten.isEmpty())
        assertTrue(DemoSettingsStore.unwritten(book, quest).isEmpty())
        // A change back to the Hub's value cancels the unwritten one.
        book = DemoSettingsStore.save(book, quest, "boxing", mapOf("haptics_db" to "4"))
        book = DemoSettingsStore.save(book, quest, "boxing", mapOf("haptics_db" to "6"))
        assertTrue(DemoSettingsStore.entry(book, quest, "boxing")!!.unwritten.isEmpty())
    }

    @Test
    fun readingKeepsUnwrittenChangesTheHubDoesNotHave() {
        var book = DemoSettingsStore.save(emptyMap(), quest, "boxing", mapOf("haptics_db" to "9"))
        book = DemoSettingsStore.onRead(book, quest, "boxing", mapOf("round" to "55", "haptics_db" to "2"))
        val entry = DemoSettingsStore.entry(book, quest, "boxing")!!
        assertEquals(mapOf("haptics_db" to "9"), entry.unwritten)
        assertEquals("9", entry.values["haptics_db"])
    }

    @Test
    fun onlyTheTableOptionsAndValuesAreKept() {
        val book = DemoSettingsStore.onRead(emptyMap(), quest, "boxing", mapOf("haptics_db" to "-2", "speed" to "fast", "round" to "30"))
        assertEquals(mapOf("round" to "30"), DemoSettingsStore.entry(book, quest, "boxing")!!.hub)
        assertTrue(DemoSettingsStore.decode("").isEmpty())
        assertTrue(DemoSettingsStore.decode("{").isEmpty())
        assertTrue(DemoSettingsStore.decode("""{"version":2,"quests":{}}""").isEmpty())
        val stored = """{"version":1,"quests":{"ip:192.168.1.21":{"boxing":{"hub":{"round":"30","speed":"x"},"unwritten":{"haptics_db":"7"}},"unknown-demo":{"hub":{}}},"bad":{}}}"""
        assertEquals(mapOf("ip:192.168.1.21" to mapOf("boxing" to DemoSettingsEntry(mapOf("round" to "30")))), DemoSettingsStore.decode(stored))
    }

    @Test
    fun aChangeSavedBeforeTheSerialWasKnownMovesToTheSerial() {
        val byIp = DemoSettingsStore.questKeys("", "192.168.1.20")
        var book = DemoSettingsStore.save(emptyMap(), byIp, "boxing", mapOf("haptics_db" to "4"))
        assertEquals("Found under the IP once the serial is known.", mapOf("haptics_db" to "4"), DemoSettingsStore.entry(book, quest, "boxing")!!.unwritten)
        book = DemoSettingsStore.onWritten(book, quest, "boxing", mapOf("haptics_db" to "4"))
        assertEquals(setOf("serial:2G0YC5ZG8N01AB"), book.keys)
    }

    @Test
    fun presetsDropDeviceOptions() {
        val trim = DemoCatalog.optionsFor("boxing").first { it.id == "haptics_db" }
        assertTrue(trim.device)
        assertEquals(listOf("round"), DemoCatalog.stepOptionsFor("boxing", emptyMap()).map { it.id })
        assertEquals(mapOf("round" to "30"), DemoCatalog.applicableOptions("boxing", mapOf("round" to "30", "haptics_db" to "6")))
        assertEquals(listOf(PresetStep("boxing", mapOf("round" to "30"))),
            HubPresets.normalize(listOf(PresetStep("boxing", mapOf("round" to "30", "haptics_db" to "6")))))

        val imported = PresetTransfer.parsePayload(
            """{"version":1,"presets":[{"name":"A","steps":[{"demo_id":"boxing","options":{"round":"30","haptics_db":"6"}},{"demo_id":"volley"}]}]}""",
        ) as TransferResult.Accepted
        assertEquals(listOf(PresetStep("boxing", mapOf("round" to "30")), PresetStep("volley")), imported.presets.single().steps)
        assertEquals(listOf("Boxing の触覚の強さ"), imported.droppedDeviceOptions)
        val plain = PresetTransfer.parsePayload("""{"version":1,"presets":[{"name":"A","steps":[{"demo_id":"volley"}]}]}""") as TransferResult.Accepted
        assertTrue(plain.droppedDeviceOptions.isEmpty())

        // An unwritten preset stored with one (before) is read back without it.
        val kept = UnwrittenPresets.add(emptyList(), listOf(RemotePreset("A", listOf(PresetStep("boxing", mapOf("haptics_db" to "6"))))), listOf(1))
        assertEquals(listOf(PresetStep("boxing")), UnwrittenPresets.decode(UnwrittenPresets.encode(kept)).single().preset.steps)
    }
}
