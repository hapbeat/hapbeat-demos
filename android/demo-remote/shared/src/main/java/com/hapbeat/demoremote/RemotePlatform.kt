package com.hapbeat.demoremote

import com.hapbeat.demoremote.adb.UsbBridge
import com.hapbeat.demoremote.data.KeyValueStore
import com.hapbeat.demoremote.data.MirrorSettings
import com.hapbeat.demoremote.mirror.VideoSink
import com.hapbeat.demoremote.net.PlatformNetwork
import dadb.AdbKeyPair
import java.io.File
import java.io.InputStream

/** What the shared code needs from the Android or the desktop build. */
interface RemotePlatform {
    val versionName: String
    /** Start of a new `controller_id` ([a-z0-9][a-z0-9._-]{0,63} as a whole, specs/demo-switch-control.md). */
    val controllerIdPrefix: String
    /** Where the log file lives. */
    val dataDir: File
    val settings: KeyValueStore
    val network: PlatformNetwork
    val usb: UsbBridge
    val defaultMirrorSettings: MirrorSettings
    /** The note under the mirror quality settings. */
    val mirrorDefaultsNote: String
    /** After a mirror error, try again every this many ms; null: only when the user asks. */
    val mirrorRetryMs: Long?
    /** Makes a mirror's video output; null when it only exists later ([RemoteViewModel.onMirrorSinkAvailable]). */
    val mirrorSinkFactory: (() -> VideoSink)?
    /** Asset paths shown on the licence screen, in order. */
    val licensePaths: List<String>
    /** Diagnostics line 1 before the address, e.g. "スマホ Wi-Fi". */
    val networkLabel: String
    val networkMissingText: String
    /** The selected Quest's status: 前面 on a line of its own, the marks wrapping below it (a resizable window); else one row. */
    val questStatusOnTwoLines: Boolean

    /** Monotonic clock in ms (no relation to wall time). */
    fun monotonicMs(): Long
    /** This app's adb key; generates it on first use. */
    fun loadAdbKeyPair(): AdbKeyPair
    /** Thumbnails, licence texts, scrcpy-server.jar. */
    fun openAsset(path: String): InputStream
    fun copyToClipboard(label: String, text: String)
}
