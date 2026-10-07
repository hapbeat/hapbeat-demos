package com.hapbeat.demoremote

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class QuestVisibilityTest {
    private fun quest(last: Boolean? = false, prev: Boolean? = false, port: Boolean? = false, adb: AdbState = AdbState.DISCONNECTED, serial: String = "") =
        QuestState("192.168.0.10", "HMD #1", respondedLastRound = last, respondedPrevRound = prev, adbPortOpen = port, adb = adb, serial = serial)

    @Test
    fun goneWhenNothingSeen() {
        assertFalse(QuestVisibility.isVisible(quest()))
        assertFalse(QuestVisibility.isVisible(quest(port = null)))
    }

    @Test
    fun eitherOfTheLastTwoRounds() {
        assertTrue(QuestVisibility.isVisible(quest(last = true)))
        assertTrue(QuestVisibility.isVisible(quest(prev = true)))
    }

    @Test
    fun roundsNotRunYetDoNotDropIt() {
        assertTrue(QuestVisibility.isVisible(quest(last = null, prev = null, port = null)))
        assertTrue(QuestVisibility.isVisible(quest(last = false, prev = null)))
    }

    @Test
    fun openPortInTheLastScan() {
        assertTrue(QuestVisibility.isVisible(quest(port = true)))
    }

    @Test
    fun adbOtherThanDisconnected() {
        assertTrue(QuestVisibility.isVisible(quest(adb = AdbState.CONNECTED)))
        assertTrue(QuestVisibility.isVisible(quest(adb = AdbState.CONNECTING)))
        assertTrue(QuestVisibility.isVisible(quest(adb = AdbState.AUTH_WAIT)))
    }

    @Test
    fun rememberedBySerialFirst() {
        assertTrue(QuestVisibility.matchesRemembered(quest(serial = "1WMHH"), "1WMHH", ""))
        assertFalse(QuestVisibility.matchesRemembered(quest(serial = ""), "1WMHH", "192.168.0.10"))
        assertFalse(QuestVisibility.matchesRemembered(quest(serial = "OTHER"), "1WMHH", ""))
    }

    @Test
    fun rememberedByIpWithoutSerial() {
        assertTrue(QuestVisibility.matchesRemembered(quest(), "", "192.168.0.10"))
        assertFalse(QuestVisibility.matchesRemembered(quest(), "", "192.168.0.11"))
        assertFalse(QuestVisibility.matchesRemembered(quest(), "", ""))
    }
}
