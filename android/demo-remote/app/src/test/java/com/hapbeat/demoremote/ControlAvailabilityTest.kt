package com.hapbeat.demoremote

import com.hapbeat.demoremote.protocol.DemoSwitchMessage
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Which 操作 buttons the last STATE allows, and the HMD list's editor / device model handling. */
class ControlAvailabilityTest {
    private fun state(
        demo: String = "handdemo", stepIndex: Long = -1, screen: String? = null, handStyle: String? = null,
    ) = DemoSwitchMessage.State(
        "remote-pixel", "0123456789abcdef", demo, foreground = true, hapticsOn = true, hapticsUi = false, recenterUi = false,
        paused = false, stepIndex = stepIndex, stepCount = if (stepIndex >= 0) 3 else 0, auth = null, screen = screen, handStyle = handStyle,
    )

    private fun action(name: String) = ControlCatalog.groups.flatMap { it.actions }.first { it.action == name }

    private fun reason(name: String, s: DemoSwitchMessage.State?) = ControlCatalog.unavailableReason(action(name), s)

    @Test
    fun handStyleNeedsTheSharedHands() {
        assertNotNull(reason("hand_style_skin", null))
        assertNotNull(reason("hand_style_skin", state()))
        assertNull(reason("hand_style_skin", state(handStyle = "ghost")))
        assertNull(reason("hand_style_ghost", state(handStyle = "skin")))
    }

    @Test
    fun sessionButtons() {
        assertNotNull(reason("session_next", state()))
        assertNull(reason("session_next", state(stepIndex = 0)))
        assertNotNull(reason("session_retry", state(stepIndex = 0, screen = "main")))
        assertNotNull(reason("session_retry", state(stepIndex = 0)))
        assertNull(reason("session_retry", state(stepIndex = 0, screen = "completion")))
        // The Hub's finish screen is also `completion`, but it is not a session step.
        assertNotNull(reason("session_retry", state(demo = DemoCatalog.HUB_ID, screen = "completion")))
    }

    @Test
    fun hubButtons() {
        assertNotNull(reason("hub_top", state(screen = "manage")))
        assertNotNull(reason("hub_top", state(demo = DemoCatalog.HUB_ID)))
        assertNotNull(reason("hub_top", state(demo = DemoCatalog.HUB_ID, screen = "main")))
        assertNull(reason("hub_top", state(demo = DemoCatalog.HUB_ID, screen = "manage")))
        assertNull(reason("hub_top", state(demo = DemoCatalog.HUB_ID, screen = "completion")))
        assertNotNull(reason("hub_replay", state(demo = DemoCatalog.HUB_ID, screen = "manage")))
        assertNull(reason("hub_replay", state(demo = DemoCatalog.HUB_ID, screen = "completion")))
        assertEquals(DemoCatalog.HUB_ID, action("hub_top").demoId)
    }

    @Test
    fun otherActionsDoNotDependOnState() {
        assertNull(reason("recenter", null))
        assertNull(reason("menu_open", null))
    }

    @Test
    fun editorsAreNotListedByDefault() {
        val editor = QuestState("192.168.0.20", "未確認 .20", editor = true)
        val headset = QuestState("192.168.0.37", "HMD #1", editor = false)
        val unknown = QuestState("192.168.0.38", "未確認 .38")
        assertFalse(QuestVisibility.isListed(editor, showEditors = false, selectedIp = null, nowMs = 0))
        assertTrue(QuestVisibility.isListed(editor, showEditors = true, selectedIp = null, nowMs = 0))
        assertTrue(QuestVisibility.isListed(editor, showEditors = false, selectedIp = "192.168.0.20", nowMs = 0))
        assertTrue(QuestVisibility.isListed(headset, showEditors = false, selectedIp = null, nowMs = 0))
        assertTrue(QuestVisibility.isListed(unknown, showEditors = false, selectedIp = null, nowMs = 0))
    }

    @Test
    fun newResponderWaitsForItsState() {
        // Added by a HERE at t=10 000 ms: hidden until STATE (or adb) identifies it, at most STATE_WAIT_MS.
        val fresh = QuestState("192.168.0.67", "未確認 .67", addedAtMs = 10_000)
        assertFalse(QuestVisibility.isListed(fresh, showEditors = false, selectedIp = null, nowMs = 10_500))
        assertTrue(QuestVisibility.isListed(fresh, showEditors = false, selectedIp = null, nowMs = 11_500))
        assertFalse(QuestVisibility.isListed(fresh.copy(editor = true), showEditors = false, selectedIp = null, nowMs = 10_500))
        assertFalse(QuestVisibility.isListed(fresh.copy(editor = true), showEditors = false, selectedIp = null, nowMs = 99_000))
        assertTrue(QuestVisibility.isListed(fresh.copy(editor = false, deviceModel = "Oculus Quest 3S"), showEditors = false, selectedIp = null, nowMs = 10_500))
        assertTrue(QuestVisibility.isListed(fresh, showEditors = true, selectedIp = null, nowMs = 10_500))
    }

    @Test
    fun modelLabels() {
        assertEquals("Quest 3 · .37", QuestVisibility.modelLabel("Oculus Quest 3", "192.168.0.37"))
        assertEquals("Quest 3S · .5", QuestVisibility.modelLabel("Meta Quest 3S", "10.0.0.5"))
        assertEquals("Windows PC · .20", QuestVisibility.modelLabel("Windows PC", "192.168.0.20"))
        assertTrue(QuestVisibility.isAutoLabel("未確認 .37", "192.168.0.37", null))
        assertTrue(QuestVisibility.isAutoLabel("Quest 3 · .37", "192.168.0.37", "Oculus Quest 3"))
        assertFalse(QuestVisibility.isAutoLabel("HMD #2", "192.168.0.37", "Oculus Quest 3"))
        assertFalse(QuestVisibility.isAutoLabel("Quest 3 · .37", "192.168.0.37", null))
    }
}
