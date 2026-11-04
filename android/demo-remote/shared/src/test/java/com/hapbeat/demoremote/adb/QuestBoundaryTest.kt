package com.hapbeat.demoremote.adb

import org.junit.Assert.assertEquals
import org.junit.Test

class QuestBoundaryTest {
    @Test
    fun commands() {
        assertEquals("setprop debug.oculus.guardian_pause 1", QuestBoundary.setCommand(paused = true))
        assertEquals("setprop debug.oculus.guardian_pause 0", QuestBoundary.setCommand(paused = false))
        assertEquals("getprop debug.oculus.guardian_pause", QuestBoundary.GET_COMMAND)
    }

    @Test
    fun parsesGetprop() {
        assertEquals(BoundaryState.PAUSED, QuestBoundary.parse("1\n"))
        assertEquals(BoundaryState.ACTIVE, QuestBoundary.parse("0\r\n"))
        // Unset property: getprop prints an empty line, and the boundary is on.
        assertEquals(BoundaryState.ACTIVE, QuestBoundary.parse("\n"))
        assertEquals(BoundaryState.ACTIVE, QuestBoundary.parse(""))
        assertEquals(BoundaryState.UNKNOWN, QuestBoundary.parse(null))
        assertEquals(BoundaryState.UNKNOWN, QuestBoundary.parse("2"))
    }

    @Test
    fun labels() {
        assertEquals("なし", QuestBoundary.label(BoundaryState.PAUSED))
        assertEquals("あり", QuestBoundary.label(BoundaryState.ACTIVE))
        assertEquals("不明", QuestBoundary.label(BoundaryState.UNKNOWN))
    }
}
