package com.hapbeat.demoremote.data

import com.hapbeat.demoremote.DemoCatalog
import org.junit.Assert.assertEquals
import org.junit.Test

class PresetsTest {
    private val prefix = "am start -n jp.hapbeat.demohub/com.unity3d.player.UnityPlayerGameActivity --es com.hapbeat.demo_hub.start "

    @Test
    fun planCommandMatchesContractShape() = assertEquals(
        prefix + """'{"version":1,"steps":[{"demo_id":"handdemo","options":{"tutorial":"on"}},{"demo_id":"trex-encounter"}]}'""",
        DemoCatalog.hubPlanCommand(listOf(PresetStep("handdemo", "on"), PresetStep("trex-encounter"))),
    )

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
        val command = DemoCatalog.hubPlanCommand(List(32) { PresetStep("trex-encounter", "off") })
        val json = command.substringAfter("'").substringBeforeLast("'")
        assert(json.toByteArray().size <= DemoCatalog.HUB_START_MAX_BYTES)
    }

    @Test
    fun codecRoundTrip() {
        val presets = listOf(
            RemotePreset("展示 A", listOf(PresetStep("handdemo", "on"), PresetStep("boxing"))),
            RemotePreset("短縮", listOf(PresetStep("volley", "off"))),
        )
        assertEquals(presets, PresetCodec.decode(PresetCodec.encode(presets)))
        assertEquals(emptyList<RemotePreset>(), PresetCodec.decode("not json"))
    }
}
