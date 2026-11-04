package com.hapbeat.demoremote

import android.app.Application
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.SharedPreferences
import android.os.SystemClock
import com.hapbeat.demoremote.adb.AdbKeys
import com.hapbeat.demoremote.adb.AndroidUsbBridge
import com.hapbeat.demoremote.adb.UsbBridge
import com.hapbeat.demoremote.data.KeyValueEditor
import com.hapbeat.demoremote.data.KeyValueStore
import com.hapbeat.demoremote.data.MirrorSettings
import com.hapbeat.demoremote.net.AndroidNetwork
import com.hapbeat.demoremote.net.PlatformNetwork
import dadb.AdbKeyPair
import java.io.File
import java.io.InputStream

/** The word for this device in the shared screens' texts. */
const val DEVICE_WORD = "スマホ"

class AndroidPlatform(private val app: Application) : RemotePlatform {
    override val versionName: String = BuildConfig.VERSION_NAME
    override val controllerIdPrefix = "android-"
    override val dataDir: File = app.filesDir
    override val settings: KeyValueStore = SharedPreferencesStore(app.getSharedPreferences("demo_remote", Context.MODE_PRIVATE))
    override val network: PlatformNetwork = AndroidNetwork(app)
    override val usb: UsbBridge = AndroidUsbBridge(app)
    override val defaultMirrorSettings = MirrorSettings()
    override val mirrorDefaultsNote = "既定は 1024 / 8 Mbps / 30 fps（廉価タブレットでも詰まりにくい値）。"
    override val mirrorRetryMs: Long? = null
    override val mirrorSinkFactory = null
    override val licensePaths = listOf("licenses/NOTICES.txt", "licenses/APACHE-2.0.txt")
    override val networkLabel = "スマホ Wi-Fi"
    override val networkMissingText = "Wi-Fi 未接続"

    override fun monotonicMs(): Long = SystemClock.elapsedRealtime()
    override fun loadAdbKeyPair(): AdbKeyPair = AdbKeys.load(app.filesDir)
    override fun openAsset(path: String): InputStream = app.assets.open(path)
    override fun copyToClipboard(label: String, text: String) {
        app.getSystemService(ClipboardManager::class.java)?.setPrimaryClip(ClipData.newPlainText(label, text))
    }
}

private class SharedPreferencesStore(private val prefs: SharedPreferences) : KeyValueStore {
    override fun getString(key: String): String? = prefs.getString(key, null)
    override fun getInt(key: String, default: Int): Int = prefs.getInt(key, default)
    override fun getLong(key: String, default: Long): Long = prefs.getLong(key, default)
    override fun getBoolean(key: String, default: Boolean): Boolean = prefs.getBoolean(key, default)

    override fun write(durable: Boolean, block: KeyValueEditor.() -> Unit): Boolean {
        val edit = prefs.edit()
        object : KeyValueEditor {
            override fun putString(key: String, value: String) { edit.putString(key, value) }
            override fun putInt(key: String, value: Int) { edit.putInt(key, value) }
            override fun putLong(key: String, value: Long) { edit.putLong(key, value) }
            override fun putBoolean(key: String, value: Boolean) { edit.putBoolean(key, value) }
            override fun remove(key: String) { edit.remove(key) }
        }.block()
        if (durable) return edit.commit()
        edit.apply()
        return true
    }
}
