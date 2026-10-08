package com.hapbeat.demoremote.data

import android.content.Context
import android.content.SharedPreferences
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
        val MAX_SIZES = listOf(720, 1024, 1280, 1600)
        val BIT_RATES = listOf(2_000_000, 4_000_000, 8_000_000)
        val FPS = listOf(30, 60)
    }
}

/** SharedPreferences-backed settings. The shared secret is never logged or shown. */
class SettingsStore(context: Context) : SequenceStore {
    private val prefs: SharedPreferences = context.getSharedPreferences("demo_remote", Context.MODE_PRIVATE)

    init {
        if (prefs.getString(KEY_CONTROLLER_ID, null) == null) regenerateControllerId()
    }

    val controllerId: String get() = prefs.getString(KEY_CONTROLLER_ID, null)!!

    /** New `android-xxxxxxxx` ID; the sequence restarts at 1. */
    fun regenerateControllerId(): Boolean {
        val bytes = ByteArray(4).also { SecureRandom().nextBytes(it) }
        val id = "android-" + bytes.joinToString("") { "%02x".format(it) }
        return prefs.edit().putString(KEY_CONTROLLER_ID, id).putLong(KEY_NEXT_SEQ, 1).commit()
    }

    override fun readNext(): Long = prefs.getLong(KEY_NEXT_SEQ, 1)
    override fun writeNextSync(next: Long): Boolean = prefs.edit().putLong(KEY_NEXT_SEQ, next).commit()

    // ---- authentication ----
    val authConfig: AuthConfig
        get() = AuthConfig(secret = prefs.getString(KEY_SECRET, null), allowUnsigned = prefs.getBoolean(KEY_ALLOW_UNSIGNED, false))

    /** True once the user has chosen secret or unsigned mode on first launch. */
    val authChosen: Boolean get() = prefs.getBoolean(KEY_AUTH_CHOSEN, false)

    fun setSecret(secret: String) {
        prefs.edit().putString(KEY_SECRET, secret).putBoolean(KEY_ALLOW_UNSIGNED, false).putBoolean(KEY_AUTH_CHOSEN, true).apply()
    }

    fun clearSecret() {
        prefs.edit().remove(KEY_SECRET).apply()
    }

    fun setAllowUnsigned(allow: Boolean) {
        prefs.edit().putBoolean(KEY_ALLOW_UNSIGNED, allow).putBoolean(KEY_AUTH_CHOSEN, true).apply()
    }

    // ---- last selected Quest (the list itself is not saved) ----
    /** Serial of the Quest chosen last; "" when none or chosen before its serial was known. */
    val rememberedSerial: String get() = prefs.getString(KEY_SELECTED_SERIAL, null) ?: ""

    /** IP of the Quest chosen last, used only while its serial is unknown; "" when none. */
    val rememberedIp: String get() = prefs.getString(KEY_SELECTED_IP, null) ?: ""

    /** Keeps [serial] when known, else [ip]. */
    fun rememberSelection(serial: String, ip: String) {
        val edit = prefs.edit()
        if (serial.isNotEmpty()) edit.putString(KEY_SELECTED_SERIAL, serial).remove(KEY_SELECTED_IP)
        else edit.putString(KEY_SELECTED_IP, ip).remove(KEY_SELECTED_SERIAL)
        edit.apply()
    }

    fun forgetSelection() {
        prefs.edit().remove(KEY_SELECTED_SERIAL).remove(KEY_SELECTED_IP).apply()
    }

    // ---- mirror / display ----
    var mirrorSettings: MirrorSettings
        get() = MirrorSettings(
            maxSize = prefs.getInt(KEY_MIRROR_MAX_SIZE, 1024),
            bitRate = prefs.getInt(KEY_MIRROR_BIT_RATE, 8_000_000),
            maxFps = prefs.getInt(KEY_MIRROR_FPS, 30),
            bothEyes = prefs.getBoolean(KEY_MIRROR_BOTH_EYES, false),
        )
        set(value) {
            prefs.edit().putInt(KEY_MIRROR_MAX_SIZE, value.maxSize).putInt(KEY_MIRROR_BIT_RATE, value.bitRate)
                .putInt(KEY_MIRROR_FPS, value.maxFps).putBoolean(KEY_MIRROR_BOTH_EYES, value.bothEyes).apply()
        }

    var mirrorEnabled: Boolean
        get() = prefs.getBoolean(KEY_MIRROR_ENABLED, false)
        set(value) { prefs.edit().putBoolean(KEY_MIRROR_ENABLED, value).apply() }

    var keepScreenOn: Boolean
        get() = prefs.getBoolean(KEY_KEEP_SCREEN_ON, true)
        set(value) { prefs.edit().putBoolean(KEY_KEEP_SCREEN_ON, value).apply() }

    /** List responders that run inside a development editor (STATE `editor`) too. */
    var showEditors: Boolean
        get() = prefs.getBoolean(KEY_SHOW_EDITORS, false)
        set(value) { prefs.edit().putBoolean(KEY_SHOW_EDITORS, value).apply() }

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
    }
}
