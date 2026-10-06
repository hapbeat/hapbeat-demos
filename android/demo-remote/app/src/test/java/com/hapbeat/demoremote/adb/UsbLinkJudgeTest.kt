package com.hapbeat.demoremote.adb

import org.junit.Assert.assertEquals
import org.junit.Test

class UsbLinkJudgeTest {
    private fun judge(adb: Boolean = false, others: Int = 0, connected: Boolean? = null, host: Boolean? = null, plugged: Boolean? = null) =
        UsbLinkJudge.judge(adb, others, connected, host, plugged)

    @Test
    fun attachedDevicesDecideFirst() {
        assertEquals(UsbLink.HOST_QUEST, judge(adb = true, others = 1, connected = true, host = false))
        assertEquals(UsbLink.HOST_NO_ADB, judge(others = 1))
    }

    @Test
    fun usbStateTellsPeripheral() {
        assertEquals(UsbLink.PERIPHERAL, judge(connected = true, host = false))
        assertEquals(UsbLink.NONE, judge(connected = false, plugged = true))
        assertEquals(UsbLink.NONE, judge(connected = true, host = true))
    }

    @Test
    fun batteryOnlyWhenUsbStateUndecided() {
        assertEquals(UsbLink.PERIPHERAL, judge(plugged = true))
        assertEquals(UsbLink.PERIPHERAL, judge(connected = true, plugged = true))
        assertEquals(UsbLink.NONE, judge(connected = true))
        assertEquals(UsbLink.NONE, judge(plugged = false))
        assertEquals(UsbLink.NONE, judge())
    }

    @Test
    fun texts() {
        assertEquals("Quest は見えていますが USB デバッグが無効です（Quest の開発者モードを確認）", UsbLinkJudge.notFoundText(UsbLink.HOST_NO_ADB))
        assertEquals(UsbLinkJudge.notFoundText(UsbLink.NONE), UsbLinkJudge.notFoundText(UsbLink.HOST_QUEST))
        assertEquals("スマホが周辺機器側（Quest 側がホスト）", UsbLinkJudge.statusText(UsbLink.PERIPHERAL))
    }
}
