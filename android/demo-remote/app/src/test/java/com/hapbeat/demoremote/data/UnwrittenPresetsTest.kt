package com.hapbeat.demoremote.data

import com.hapbeat.demoremote.DemoCatalog
import com.hapbeat.demoremote.protocol.DemoSwitchMessage
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** demo-session.md「リモコンへのプリセット受け渡し」: imported presets kept on the phone until written; the Hub-front check. */
class UnwrittenPresetsTest {
    private val volley = RemotePreset("展示 A", listOf(PresetStep("volley", mapOf("scene" to "match"), retry = false), PresetStep("handdemo")))
    private val short = RemotePreset("短縮コース", listOf(PresetStep("trex-encounter")))

    @Test
    fun roundTripKeepsOptionsRetryAndSlot() {
        val items = UnwrittenPresets.add(emptyList(), listOf(volley, short), listOf(3, 1))
        assertEquals(listOf(1L, 2L), items.map { it.id })
        assertEquals(items, UnwrittenPresets.decode(UnwrittenPresets.encode(items)))
        val more = UnwrittenPresets.add(items.drop(1), listOf(volley), listOf(2))
        assertEquals("New ids follow the largest kept one.", listOf(2L, 3L), more.map { it.id })
    }

    @Test
    fun brokenStorageReadsAsEmpty() {
        assertTrue(UnwrittenPresets.decode("").isEmpty())
        assertTrue(UnwrittenPresets.decode("{").isEmpty())
        assertTrue(UnwrittenPresets.decode("""{"version":2,"items":[]}""").isEmpty())
    }

    @Test
    fun entriesThatNoLongerPassAreDroppedOthersKept() {
        val good = """{"id":1,"slot":2,"preset":{"name":"a","steps":[{"demo_id":"handdemo"}]}}"""
        val badSlot = """{"id":2,"slot":4,"preset":{"name":"b","steps":[{"demo_id":"handdemo"}]}}"""
        val unknownDemo = """{"id":3,"slot":1,"preset":{"name":"c","steps":[{"demo_id":"not-a-demo"}]}}"""
        val badOption = """{"id":4,"slot":1,"preset":{"name":"d","steps":[{"demo_id":"volley","options":{"scene":"nope"}}]}}"""
        val extraField = """{"id":5,"slot":1,"preset":{"name":"e","steps":[{"demo_id":"handdemo","x":1}]}}"""
        val duplicateId = """{"id":1,"slot":3,"preset":{"name":"f","steps":[{"demo_id":"handdemo"}]}}"""
        val text = """{"version":1,"items":[$good,$badSlot,$unknownDemo,$badOption,$extraField,$duplicateId]}"""
        assertEquals(listOf(UnwrittenPreset(1, RemotePreset("a", listOf(PresetStep("handdemo"))), 2)), UnwrittenPresets.decode(text))
    }

    private fun state(demo: String, foreground: Boolean = true, screen: String? = "main") = DemoSwitchMessage.State(
        "remote-pixel", "0123456789abcdef", demo, foreground = foreground, hapticsOn = true, hapticsUi = false, recenterUi = false,
        paused = false, stepIndex = -1, stepCount = 0, auth = null, screen = screen, handStyle = null,
    )

    @Test
    fun hubFrontReasonsAreDistinguished() {
        assertNull(HubPresets.hubFrontProblem(state(DemoCatalog.HUB_ID), DemoCatalog.HUB_ID))
        assertEquals(HubPresets.HUB_NOT_FOREGROUND, HubPresets.hubFrontProblem(state(DemoCatalog.HUB_ID, foreground = false), DemoCatalog.HUB_ID))
        assertEquals(HubPresets.HUB_MANAGE_OPEN, HubPresets.hubFrontProblem(state(DemoCatalog.HUB_ID, screen = "manage"), DemoCatalog.HUB_ID))
        assertEquals(HubPresets.otherDemoInFront("handdemo"), HubPresets.hubFrontProblem(state("handdemo"), "handdemo"))
        // No STATE: what the last HERE said.
        assertEquals(HubPresets.HUB_STATE_UNKNOWN, HubPresets.hubFrontProblem(null, DemoCatalog.HUB_ID))
        assertEquals(HubPresets.otherDemoInFront("volley"), HubPresets.hubFrontProblem(null, "volley"))
        assertEquals(HubPresets.HUB_NO_RESPONSE, HubPresets.hubFrontProblem(null, ""))
        assertTrue(HubPresets.otherDemoInFront("handdemo").contains(DemoCatalog.labelFor("handdemo")))
    }
}
