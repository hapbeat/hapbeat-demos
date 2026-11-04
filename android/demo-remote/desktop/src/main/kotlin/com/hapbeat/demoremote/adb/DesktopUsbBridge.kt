package com.hapbeat.demoremote.adb

/** Placeholder until the adb.exe bridge. */
class DesktopUsbBridge : UsbBridge {
    override val texts = UsbTexts("", emptyList(), "", "", 120, "")
    override fun readLink() = UsbLink.NONE
    override fun statusText(link: UsbLink) = ""
    override fun idleStatus(link: UsbLink) = "" to false
    override fun startWatch(onEvent: (attached: Boolean) -> Unit) = Unit
    override fun stopWatch() = Unit
    override suspend fun enableWifiAdb(link: UsbLink, progress: UsbProgress): UsbWifiAdbResult = throw UsbAdbException("未対応")
}
