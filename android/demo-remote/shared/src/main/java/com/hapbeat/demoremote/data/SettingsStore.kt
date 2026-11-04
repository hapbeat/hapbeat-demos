package com.hapbeat.demoremote.data

import com.hapbeat.demoremote.protocol.AuthConfig
import com.hapbeat.demoremote.protocol.SequenceStore
import java.security.SecureRandom

data class MirrorSettings(
    val maxSize: Int = 1024,
    val bitRate: Int = 8_000_000,
    val maxFps: Int = 30,
    val bothEyes: Boolean = false,
) {
    companion object {
        val MAX_SIZES = listOf(720, 1024, 1280, 1600, 1920)
        val BIT_RATES = listOf(2_000_000, 4_000_000, 8_000_000, 16_000_000)
        val FPS = listOf(30, 60)
    }
}

/** One batch of changes to a [KeyValueStore]. */
interface KeyValueEditor {
    fun putString(key: String, value: String)
    fun putInt(key: String, value: Int)
    fun putLong(key: String, value: Long)
    fun putBoolean(key: String, value: Boolean)
    fun remove(key: String)
}

/** Small persistent key-value storage (Android: SharedPreferences; desktop: a properties file). */
interface KeyValueStore {
    fun getString(key: String): String?
    fun getInt(key: String, default: Int): Int
    fun getLong(key: String, default: Long): Long
    fun getBoolean(key: String, default: Boolean): Boolean
    /** Applies [block] at once. [durable]: on disk before returning, with the result; otherwise written soon and true. */
    fun write(durable: Boolean = false, block: KeyValueEditor.() -> Unit): Boolean
}

/** Settings over a [KeyValueStore]. The shared secret is never logged or shown. */
class SettingsStore(
    private val prefs: KeyValueStore,
    private val controllerIdPrefix: String,
    private val mirrorDefaults: MirrorSettings,
) : SequenceStore {
    init {
        if (prefs.getString(KEY_CONTROLLER_ID) == null) regenerateControllerId()
    }

    val controllerId: String get() = prefs.getString(KEY_CONTROLLER_ID)!!

    /** New `<prefix>xxxxxxxx` ID; the sequence restarts at 1. */
    fun regenerateControllerId(): Boolean {
        val bytes = ByteArray(4).also { SecureRandom().nextBytes(it) }
        val id = controllerIdPrefix + bytes.joinToString("") { "%02x".format(it) }
        return prefs.write(durable = true) { putString(KEY_CONTROLLER_ID, id); putLong(KEY_NEXT_SEQ, 1) }
    }

    override fun readNext(): Long = prefs.getLong(KEY_NEXT_SEQ, 1)
    override fun writeNextSync(next: Long): Boolean = prefs.write(durable = true) { putLong(KEY_NEXT_SEQ, next) }

    // ---- authentication ----
    val authConfig: AuthConfig
        get() = AuthConfig(secret = prefs.getString(KEY_SECRET), allowUnsigned = prefs.getBoolean(KEY_ALLOW_UNSIGNED, false))

    /** True once the user has chosen secret or unsigned mode on first launch. */
    val authChosen: Boolean get() = prefs.getBoolean(KEY_AUTH_CHOSEN, false)

    fun setSecret(secret: String) {
        prefs.write { putString(KEY_SECRET, secret); putBoolean(KEY_ALLOW_UNSIGNED, false); putBoolean(KEY_AUTH_CHOSEN, true) }
    }

    fun clearSecret() {
        prefs.write { remove(KEY_SECRET) }
    }

    fun setAllowUnsigned(allow: Boolean) {
        prefs.write { putBoolean(KEY_ALLOW_UNSIGNED, allow); putBoolean(KEY_AUTH_CHOSEN, true) }
    }

    // ---- last selected Quest (the list itself is not saved) ----
    /** Serial of the Quest chosen last; "" when none or chosen before its serial was known. */
    val rememberedSerial: String get() = prefs.getString(KEY_SELECTED_SERIAL) ?: ""

    /** IP of the Quest chosen last, used only while its serial is unknown; "" when none. */
    val rememberedIp: String get() = prefs.getString(KEY_SELECTED_IP) ?: ""

    /** Keeps [serial] when known, else [ip]. */
    fun rememberSelection(serial: String, ip: String) {
        prefs.write {
            if (serial.isNotEmpty()) { putString(KEY_SELECTED_SERIAL, serial); remove(KEY_SELECTED_IP) }
            else { putString(KEY_SELECTED_IP, ip); remove(KEY_SELECTED_SERIAL) }
        }
    }

    fun forgetSelection() {
        prefs.write { remove(KEY_SELECTED_SERIAL); remove(KEY_SELECTED_IP) }
    }

    // ---- mirror / display ----
    var mirrorSettings: MirrorSettings
        get() = MirrorSettings(
            maxSize = prefs.getInt(KEY_MIRROR_MAX_SIZE, mirrorDefaults.maxSize),
            bitRate = prefs.getInt(KEY_MIRROR_BIT_RATE, mirrorDefaults.bitRate),
            maxFps = prefs.getInt(KEY_MIRROR_FPS, mirrorDefaults.maxFps),
            bothEyes = prefs.getBoolean(KEY_MIRROR_BOTH_EYES, mirrorDefaults.bothEyes),
        )
        set(value) {
            prefs.write {
                putInt(KEY_MIRROR_MAX_SIZE, value.maxSize); putInt(KEY_MIRROR_BIT_RATE, value.bitRate)
                putInt(KEY_MIRROR_FPS, value.maxFps); putBoolean(KEY_MIRROR_BOTH_EYES, value.bothEyes)
            }
        }

    var mirrorEnabled: Boolean
        get() = prefs.getBoolean(KEY_MIRROR_ENABLED, false)
        set(value) { prefs.write { putBoolean(KEY_MIRROR_ENABLED, value) } }

    var keepScreenOn: Boolean
        get() = prefs.getBoolean(KEY_KEEP_SCREEN_ON, true)
        set(value) { prefs.write { putBoolean(KEY_KEEP_SCREEN_ON, value) } }

    /** List responders that run inside a development editor (STATE `editor`) too. */
    var showEditors: Boolean
        get() = prefs.getBoolean(KEY_SHOW_EDITORS, false)
        set(value) { prefs.write { putBoolean(KEY_SHOW_EDITORS, value) } }

    /** Pause the Quest boundary (`debug.oculus.guardian_pause 1`) every time adb connects; on by default. */
    var pauseBoundaryOnConnect: Boolean
        get() = prefs.getBoolean(KEY_PAUSE_BOUNDARY_ON_CONNECT, true)
        set(value) { prefs.write { putBoolean(KEY_PAUSE_BOUNDARY_ON_CONNECT, value) } }

    /** Presets imported from a QR / link and not written to a Hub yet ([UnwrittenPresets] JSON; "" = none). */
    var unwrittenPresets: String
        get() = prefs.getString(KEY_UNWRITTEN_PRESETS) ?: ""
        set(value) { prefs.write { putString(KEY_UNWRITTEN_PRESETS, value) } }

    /** The phone's copy of each Quest's per-demo settings with unwritten changes ([DemoSettingsStore] JSON; "" = none). */
    var demoSettings: String
        get() = prefs.getString(KEY_DEMO_SETTINGS) ?: ""
        set(value) { prefs.write { putString(KEY_DEMO_SETTINGS, value) } }

    private companion object {
        const val KEY_CONTROLLER_ID = "controller_id"
        const val KEY_NEXT_SEQ = "next_seq"
        const val KEY_SECRET = "shared_secret"
        const val KEY_ALLOW_UNSIGNED = "allow_unsigned"
        const val KEY_AUTH_CHOSEN = "auth_chosen"
        const val KEY_SELECTED_SERIAL = "selected_serial"
        const val KEY_SELECTED_IP = "selected_ip"
        const val KEY_MIRROR_MAX_SIZE = "mirror_max_size"
        const val KEY_MIRROR_BIT_RATE = "mirror_bit_rate"
        const val KEY_MIRROR_FPS = "mirror_fps"
        const val KEY_MIRROR_BOTH_EYES = "mirror_both_eyes"
        const val KEY_MIRROR_ENABLED = "mirror_enabled"
        const val KEY_KEEP_SCREEN_ON = "keep_screen_on"
        const val KEY_SHOW_EDITORS = "show_editors"
        const val KEY_UNWRITTEN_PRESETS = "unwritten_presets"
        const val KEY_DEMO_SETTINGS = "demo_settings"
        const val KEY_PAUSE_BOUNDARY_ON_CONNECT = "pause_boundary_on_connect"
    }
}
