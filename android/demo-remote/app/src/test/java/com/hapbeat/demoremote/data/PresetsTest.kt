package com.hapbeat.demoremote.data

import com.hapbeat.demoremote.DemoCatalog
import org.junit.Assert.assertEquals
import org.junit.Test

class PresetsTest {
    private val prefix = "am start -n jp.hapbeat.demohub/com.unity3d.player.UnityPlayerGameActivity --es com.hapbeat.demo_hub.start "

    @Test
    fun planCommandMatchesContractShape() = assertEquals(
        prefix + """'{"version":1,"steps":[{"demo_id":"energy-duel","options":{"tutorial":"on","mode":"free"}},{"demo_id":"trex-encounter","retry":false}]}'""",
        DemoCatalog.hubPlanCommand(listOf(
            PresetStep("energy-duel", linkedMapOf("tutorial" to "on", "mode" to "free")),
            PresetStep("trex-encounter", retry = false),
        )),
    )

    @Test(expected = IllegalArgumentException::class)
    fun planRejectsOptionOutsideTheTable() {
        DemoCatalog.hubPlanCommand(listOf(PresetStep("handdemo", mapOf("tutorial" to "on"))))
    }

    @Test(expected = IllegalArgumentException::class)
    fun planRejectsQuoteInOptionValue() {
        DemoCatalog.hubPlanCommand(listOf(PresetStep("volley", mapOf("scene" to "x';reboot;'"))))
    }

    @Test
    fun shellQuoteEscapesSingleQuotes() {
        assertEquals("""'a'\''b'""", DemoCatalog.shellQuote("a'b"))
        assertEquals("'{}'", DemoCatalog.shellQuote("{}"))
    }

    @Test(expected = IllegalArgumentException::class)
    fun planRejectsEmpty() {
        DemoCatalog.hubPlanCommand(emptyList())
    }

    @Test(expected = IllegalArgumentException::class)
    fun planRejectsMoreThan32() {
        DemoCatalog.hubPlanCommand(List(33) { PresetStep("handdemo") })
    }

    @Test(expected = IllegalArgumentException::class)
    fun planRejectsUnknownDemo() {
        DemoCatalog.hubPlanCommand(listOf(PresetStep("gloveball_v2")))
    }

    @Test
    fun thirtyTwoStepsFitTheExtraLimit() {
        val allOptions = mapOf("tutorial" to "off", "round_seconds" to "60", "difficulty" to "strong", "mode" to "match")
        val command = DemoCatalog.hubPlanCommand(List(32) { PresetStep("energy-duel", allOptions, retry = false) })
        val json = command.substringAfter("'").substringBeforeLast("'")
        assert(json.toByteArray().size <= DemoCatalog.HUB_START_MAX_BYTES)
    }

    @Test
    fun codecRoundTrip() {
        val presets = listOf(
            RemotePreset("展示 A", listOf(PresetStep("energy-duel", mapOf("tutorial" to "on")), PresetStep("boxing", retry = false))),
            RemotePreset("短縮", listOf(PresetStep("volley", mapOf("scene" to "receive", "balls" to "20")))),
        )
        assertEquals(presets, PresetCodec.decode(PresetCodec.encode(presets)))
        assertEquals(emptyList<RemotePreset>(), PresetCodec.decode("not json"))
    }

    @Test
    fun codecDropsTheOldTutorialFormat() {
        val old = """[{"name":"旧","steps":[{"demo_id":"handdemo","tutorial":"on"}]},{"name":"新","steps":[{"demo_id":"fps","retry":true}]}]"""
        assertEquals(listOf(RemotePreset("新", listOf(PresetStep("fps")))), PresetCodec.decode(old))
    }
}
