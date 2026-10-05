package com.hapbeat.demoremote.data

import android.content.Context
import android.content.SharedPreferences
import com.hapbeat.demoremote.protocol.AuthConfig
import com.hapbeat.demoremote.protocol.JsonArray
import com.hapbeat.demoremote.protocol.JsonException
import com.hapbeat.demoremote.protocol.JsonBool
import com.hapbeat.demoremote.protocol.JsonNumber
import com.hapbeat.demoremote.protocol.JsonObject
import com.hapbeat.demoremote.protocol.JsonString
import com.hapbeat.demoremote.protocol.MiniJson
import com.hapbeat.demoremote.protocol.SequenceStore
import java.security.SecureRandom

/** Persisted part of a Quest entry (keyed by IPv4). */
data class SavedQuest(
    val ip: String,
    val label: String,
    val model: String = "",
    val lastDemoId: String = "",
    val lastSeenAtMs: Long = 0,
    /** ro.serialno from adb: identifies the headset when DHCP hands it a different IP. */
    val serial: String = "",
    /** Added by hand in settings; kept even before adb has identified it. */
    val manual: Boolean = false,
)

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

    // ---- Quest list ----
    fun loadQuests(): List<SavedQuest> {
        val raw = prefs.getString(KEY_QUESTS, null) ?: return emptyList()
        return try {
            (MiniJson.parse(raw) as JsonArray).items.mapNotNull { item ->
                val f = (item as? JsonObject)?.fields ?: return@mapNotNull null
                SavedQuest(
                    ip = (f["ip"] as? JsonString)?.value ?: return@mapNotNull null,
                    label = (f["label"] as? JsonString)?.value ?: "",
                    model = (f["model"] as? JsonString)?.value ?: "",
                    lastDemoId = (f["last_demo_id"] as? JsonString)?.value ?: "",
                    lastSeenAtMs = (f["last_seen_at"] as? JsonNumber)?.longOrNull() ?: 0,
                    serial = (f["serial"] as? JsonString)?.value ?: "",
                    manual = (f["manual"] as? JsonBool)?.value ?: false,
                )
            }
        } catch (_: JsonException) {
            emptyList()
        } catch (_: ClassCastException) {
            emptyList()
        }
    }

    fun saveQuests(quests: List<SavedQuest>) {
        val json = MiniJson.write(quests.map {
            linkedMapOf(
                "ip" to it.ip, "label" to it.label, "model" to it.model,
                "last_demo_id" to it.lastDemoId, "last_seen_at" to it.lastSeenAtMs, "serial" to it.serial, "manual" to it.manual,
            )
        })
        prefs.edit().putString(KEY_QUESTS, json).apply()
    }

    var selectedIp: String?
        get() = prefs.getString(KEY_SELECTED_IP, null)
        set(value) { prefs.edit().putString(KEY_SELECTED_IP, value).apply() }

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

    var presets: List<RemotePreset>
        get() = PresetCodec.decode(prefs.getString(KEY_PRESETS, null))
        set(value) { prefs.edit().putString(KEY_PRESETS, PresetCodec.encode(value)).apply() }

    /** Hosts with Wi-Fi adb that turned out not to be a Quest (e.g. a phone left in `adb tcpip`). */
    var ignoredAdbHosts: Set<String>
        get() = prefs.getStringSet(KEY_IGNORED_ADB_HOSTS, emptySet())!!.toSet()
        set(value) { prefs.edit().putStringSet(KEY_IGNORED_ADB_HOSTS, value).apply() }

    var keepScreenOn: Boolean
        get() = prefs.getBoolean(KEY_KEEP_SCREEN_ON, true)
        set(value) { prefs.edit().putBoolean(KEY_KEEP_SCREEN_ON, value).apply() }

    private companion object {
        const val KEY_CONTROLLER_ID = "controller_id"
        const val KEY_NEXT_SEQ = "next_seq"
        const val KEY_SECRET = "shared_secret"
        const val KEY_ALLOW_UNSIGNED = "allow_unsigned"
        const val KEY_AUTH_CHOSEN = "auth_chosen"
        const val KEY_QUESTS = "quests"
        const val KEY_SELECTED_IP = "selected_ip"
        const val KEY_PRESETS = "remote_presets"
        const val KEY_IGNORED_ADB_HOSTS = "ignored_adb_hosts"
        const val KEY_MIRROR_MAX_SIZE = "mirror_max_size"
        const val KEY_MIRROR_BIT_RATE = "mirror_bit_rate"
        const val KEY_MIRROR_FPS = "mirror_fps"
        const val KEY_MIRROR_BOTH_EYES = "mirror_both_eyes"
        const val KEY_MIRROR_ENABLED = "mirror_enabled"
        const val KEY_KEEP_SCREEN_ON = "keep_screen_on"
    }
}
