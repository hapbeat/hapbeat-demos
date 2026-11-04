package com.hapbeat.demoremote

import com.hapbeat.demoremote.adb.AdbKeys
import com.hapbeat.demoremote.adb.UsbBridge
import com.hapbeat.demoremote.data.KeyValueStore
import com.hapbeat.demoremote.data.MirrorSettings
import com.hapbeat.demoremote.mirror.VideoSink
import com.hapbeat.demoremote.net.PlatformNetwork
import dadb.AdbKeyPair
import java.awt.Toolkit
import java.awt.datatransfer.StringSelection
import java.io.File
import java.io.FileNotFoundException
import java.io.InputStream

/** The word for this device in the shared screens' texts. */
const val DEVICE_WORD = "PC"

class DesktopPlatform(
    override val versionName: String,
    override val dataDir: File,
    override val settings: KeyValueStore,
    override val network: PlatformNetwork,
    override val usb: UsbBridge,
    override val mirrorSinkFactory: (() -> VideoSink)?,
    /** Holds an adbkey + adbkey.pub the user's Quests already trust (`~/.android`); used when present. */
    private val existingAdbKeyDir: File,
) : RemotePlatform {
    override val controllerIdPrefix = "pc-"
    override val defaultMirrorSettings = MirrorSettings(maxSize = 1920, bitRate = 16_000_000, maxFps = 30)
    override val mirrorDefaultsNote = "既定は 1920 / 16 Mbps / 30 fps。"
    override val mirrorRetryMs: Long? = MIRROR_RETRY_MS
    override val licensePaths = listOf("licenses/NOTICES.txt", "licenses/APACHE-2.0.txt")
    override val networkLabel = "PC LAN"
    override val networkMissingText = "LAN 未接続"
    override val questStatusOnTwoLines = true

    override fun monotonicMs(): Long = System.nanoTime() / 1_000_000

    override fun loadAdbKeyPair(): AdbKeyPair {
        val priv = File(existingAdbKeyDir, "adbkey")
        val pub = File(existingAdbKeyDir, "adbkey.pub")
        return if (priv.isFile && pub.isFile) AdbKeyPair.read(priv, pub) else AdbKeys.load(dataDir)
    }

    override fun openAsset(path: String): InputStream =
        javaClass.classLoader.getResourceAsStream(path) ?: throw FileNotFoundException(path)

    override fun copyToClipboard(label: String, text: String) {
        Toolkit.getDefaultToolkit().systemClipboard.setContents(StringSelection(text), null)
    }

    private companion object {
        const val MIRROR_RETRY_MS = 3_000L
    }
}
