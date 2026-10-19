package com.hapbeat.demoremote.data

import com.hapbeat.demoremote.DemoCatalog
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * demo-session.md「デモごとの設定」: the phone's copy of each Hub's per-demo settings (keyed by `hub_id`), with only the options
 * the user changed kept as unwritten until written; and the per-demo (`scope: device`) options a preset never holds.
 */
class DemoSettingsStoreTest {
    private val hub = "5c1e0a9b7d3f2468"
    private val otherHub = "0123456789abcdef"
    private val read = mapOf("round" to "55", "haptics_db" to "3")

    @Test
    fun saveWithoutTheHubKeepsOnlyTheChangedOptionAcrossARestart() {
        var book = DemoSettingsStore.onRead(emptyMap(), hub, "boxing", read)
        book = DemoSettingsStore.save(book, hub, "boxing", mapOf("haptics_db" to "6"))
        val entry = DemoSettingsStore.entry(book, hub, "boxing")!!
        assertEquals("Only what the user changed.", mapOf("haptics_db" to "6"), entry.unwritten)
        assertEquals(mapOf("round" to "55", "haptics_db" to "6"), entry.values)

        // Closing the app: the settings string is read back.
        val restored = DemoSettingsStore.decode(DemoSettingsStore.encode(book))
        assertEquals(book, restored)
        assertEquals(listOf(UnwrittenDemoSettings(hub, "boxing", mapOf("haptics_db" to "6"))), DemoSettingsStore.unwritten(restored, hub))
        assertTrue("Another Hub has its own copies.", DemoSettingsStore.unwritten(restored, otherHub).isEmpty())
        assertNull(DemoSettingsStore.entry(restored, otherHub, "boxing"))
    }

    @Test
    fun aHubNeverReadGetsNoUnwrittenChange() {
        assertTrue(DemoSettingsStore.save(emptyMap(), hub, "boxing", mapOf("haptics_db" to "6")).isEmpty())
    }

    @Test
    fun aChangeOnTheHeadsetIsNotOverwrittenByAnUntouchedValue() {
        var book = DemoSettingsStore.onRead(emptyMap(), hub, "boxing", read)
        book = DemoSettingsStore.save(book, hub, "boxing", mapOf("haptics_db" to "6"))
        // Meanwhile staff change the round on the Hub's manage screen; the next read brings it.
        book = DemoSettingsStore.onRead(book, hub, "boxing", mapOf("round" to "30", "haptics_db" to "3"))
        val entry = DemoSettingsStore.entry(book, hub, "boxing")!!
        assertEquals("The write carries only haptics_db.", mapOf("haptics_db" to "6"), entry.unwritten)
        assertEquals(mapOf("round" to "30", "haptics_db" to "6"), entry.values)
    }

    @Test
    fun writingOrReadingTheSameValueClearsTheUnwrittenChange() {
        var book = DemoSettingsStore.onRead(emptyMap(), hub, "boxing", read)
        book = DemoSettingsStore.save(book, hub, "boxing", mapOf("haptics_db" to "6", "round" to "30"))
        book = DemoSettingsStore.onWritten(book, hub, "boxing", mapOf("haptics_db" to "6"))
        assertEquals(mapOf("round" to "30"), DemoSettingsStore.entry(book, hub, "boxing")!!.unwritten)
        assertEquals("6", DemoSettingsStore.entry(book, hub, "boxing")!!.hub["haptics_db"])
        // The Hub already has round 30 (written from elsewhere): reading clears it.
        book = DemoSettingsStore.onRead(book, hub, "boxing", mapOf("round" to "30", "haptics_db" to "6"))
        assertTrue(DemoSettingsStore.unwritten(book, hub).isEmpty())
        // A change back to the Hub's value cancels the unwritten one.
        book = DemoSettingsStore.save(book, hub, "boxing", mapOf("haptics_db" to "12"))
        book = DemoSettingsStore.save(book, hub, "boxing", mapOf("haptics_db" to "6"))
        assertTrue(DemoSettingsStore.entry(book, hub, "boxing")!!.unwritten.isEmpty())
    }

    @Test
    fun onlyTheTableOptionsAndValuesAreKept() {
        val book = DemoSettingsStore.onRead(emptyMap(), hub, "boxing", mapOf("haptics_db" to "-2", "speed" to "fast", "round" to "30"))
        assertEquals(mapOf("round" to "30"), DemoSettingsStore.entry(book, hub, "boxing")!!.hub)
        assertTrue(DemoSettingsStore.decode("").isEmpty())
        assertTrue(DemoSettingsStore.decode("{").isEmpty())
        assertTrue("The serial / IP keyed copies of the d4 build are not migrated.",
            DemoSettingsStore.decode("""{"version":1,"quests":{"serial:X":{"boxing":{"hub":{"round":"30"}}}}}""").isEmpty())
        val stored = """{"version":2,"hubs":{"$hub":{"boxing":{"hub":{"round":"30","speed":"x"},"unwritten":{"haptics_db":"7"}},"unknown-demo":{"hub":{}}},"ip:1.2.3.4":{}}}"""
        assertEquals(mapOf(hub to mapOf("boxing" to DemoSettingsEntry(mapOf("round" to "30")))), DemoSettingsStore.decode(stored))
    }

    @Test
    fun presetsDropDeviceOptions() {
        val trim = DemoCatalog.optionsFor("boxing").first { it.id == "haptics_db" }
        assertTrue(trim.device)
        assertEquals("3", trim.default)
        assertEquals(listOf("0", "3", "6", "9", "12", "15", "18", "24"), trim.values.map { it.value })
        assertEquals(listOf("0 dB", "-3 dB", "-6 dB", "-9 dB", "-12 dB", "-15 dB", "-18 dB", "-24 dB"), trim.values.map { it.label })
        // A copy kept from before the 3 dB steps (-2 dB): the value is dropped, so the screen shows the default.
        val old = DemoSettingsStore.decode("""{"version":2,"hubs":{"$hub":{"boxing":{"hub":{"haptics_db":"2","round":"30"},"unwritten":{"haptics_db":"4"}}}}}""")
        assertEquals(DemoSettingsEntry(mapOf("round" to "30")), DemoSettingsStore.entry(old, hub, "boxing"))
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

    @Test
    fun demoSettingsFailuresReadAsPlainJapanese() {
        assertEquals("Hub のこのデモにない設定です（触覚の強さ。Hub とリモコンの版を確認）", HubPresets.demoSettingsFailureText("invalid_payload", "haptics_db"))
        assertEquals("Boxing が HMD の Hub に入っていないか、Hub にこのデモの設定がありません", HubPresets.demoSettingsFailureText("not_allowed", "boxing"))
        assertTrue(HubPresets.demoSettingsFailureText("not_allowed", "An operation is in progress.").startsWith("Hub が受け付けませんでした"))
        assertEquals(HubPresets.HUB_NOT_FOREGROUND, HubPresets.demoSettingsFailureText("not_allowed", "not in foreground"))
        assertEquals("Hub で保存できませんでした: disk full", HubPresets.demoSettingsFailureText("launch_failed", "disk full"))
    }
}
