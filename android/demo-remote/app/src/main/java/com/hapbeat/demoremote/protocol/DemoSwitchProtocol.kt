package com.hapbeat.demoremote.protocol

import com.hapbeat.demoremote.data.HubDemo
import com.hapbeat.demoremote.data.PresetStep
import com.hapbeat.demoremote.data.PresetTransfer
import java.security.MessageDigest
import java.security.SecureRandom
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/**
 * Demo Switch control protocol v1 (hapbeat-contracts specs/demo-switch-control.md).
 * Android independent so it is covered by JVM unit tests.
 */

sealed interface DemoSwitchMessage {
    val controllerId: String
    val auth: String?

    data class Switch(override val controllerId: String, val seq: Long, val demoId: String, override val auth: String?) : DemoSwitchMessage
    data class Control(
        override val controllerId: String, val seq: Long, val demoId: String,
        val action: String, val sceneId: String, override val auth: String?,
    ) : DemoSwitchMessage
    data class Status(
        val type: String, override val controllerId: String, val seq: Long, val demoId: String,
        val currentDemoId: String, val code: String, val message: String, override val auth: String?,
    ) : DemoSwitchMessage
    data class Discover(override val controllerId: String, val nonce: String, override val auth: String?) : DemoSwitchMessage
    data class Here(override val controllerId: String, val nonce: String, val currentDemoId: String, override val auth: String?) : DemoSwitchMessage
    data class Query(override val controllerId: String, val nonce: String, override val auth: String?) : DemoSwitchMessage
    data class State(
        override val controllerId: String, val nonce: String, val currentDemoId: String,
        /** false while the demo is running but not the focused app (menu, pause, boundary setup): SWITCH / CONTROL are refused. */
        val foreground: Boolean,
        val hapticsOn: Boolean, val hapticsUi: Boolean, val recenterUi: Boolean, val paused: Boolean,
        val stepIndex: Long, val stepCount: Long, override val auth: String?,
        /**
         * Optional fields (null = absent: an older runtime, unknown rather than an error). [deviceModel] as the OS reports
         * it, [editor] true inside a development editor, [screen] `main` / `completion` / `manage`, [handStyle]
         * `ghost` / `skin` (absent when the runtime does not draw the shared hands).
         */
        val deviceModel: String? = null, val editor: Boolean? = null, val screen: String? = null, val handStyle: String? = null,
    ) : DemoSwitchMessage

    /** Reads Hub preset [preset] (1..3) from step [from] (0..31). Answered like QUERY (no sequence). */
    data class PresetGet(override val controllerId: String, val nonce: String, val preset: Int, val from: Int, override val auth: String?) : DemoSwitchMessage

    /**
     * One page of a Hub preset: [steps] from [from] that fit in one datagram. Empty [steps] with [from] < [stepCount]
     * means the step at [from] alone does not fit (the preset can only be edited on the Hub).
     */
    data class Preset(
        override val controllerId: String, val nonce: String, val preset: Int, val revision: Long,
        val name: String, val visible: Boolean, val stepCount: Int, val from: Int, val steps: List<PresetStep>,
        override val auth: String?,
    ) : DemoSwitchMessage

    /** Overwrites Hub preset [preset]; [demoId] is always demo_hub. CONTROL rules (sequence, ACK -> READY / FAILED). */
    data class PresetSet(
        override val controllerId: String, val seq: Long, val demoId: String, val preset: Int,
        val name: String, val visible: Boolean, val steps: List<PresetStep>, override val auth: String?,
    ) : DemoSwitchMessage

    /** Starts a session from Hub preset [preset]; READY once the first demo has the foreground. */
    data class PresetStart(
        override val controllerId: String, val seq: Long, val demoId: String, val preset: Int, override val auth: String?,
    ) : DemoSwitchMessage

    /** Reads the Hub-wide settings from installed demo [from] (0..63). Answered like PRESET_GET (no sequence). */
    data class HubSettingsGet(override val controllerId: String, val nonce: String, val from: Int, override val auth: String?) : DemoSwitchMessage

    /**
     * One page of the Hub-wide settings: the four launch settings, this headset's device address ([player] / [group],
     * -1 = not specified) and the installed [demos] from [from] that fit in one datagram ([demoCount] in all).
     */
    data class HubSettings(
        override val controllerId: String, val nonce: String, val revision: Long,
        val hapticsUi: Boolean, val recenterUi: Boolean, val handStyle: String, val staffWaiting: Boolean,
        val player: Int, val group: Int, val demoCount: Int, val from: Int, val demos: List<HubDemo>,
        override val auth: String?,
    ) : DemoSwitchMessage

    /** Overwrites the Hub-wide settings; [visibleDemos] are the installed demos shown as tiles. PRESET_SET rules. */
    data class HubSettingsSet(
        override val controllerId: String, val seq: Long, val demoId: String,
        val hapticsUi: Boolean, val recenterUi: Boolean, val handStyle: String, val staffWaiting: Boolean,
        val visibleDemos: List<String>, override val auth: String?,
    ) : DemoSwitchMessage

    /** Starts a Demo Session from [steps] (1..32, not stored); READY once the first demo has the foreground. */
    data class HubStart(
        override val controllerId: String, val seq: Long, val demoId: String, val steps: List<PresetStep>, override val auth: String?,
    ) : DemoSwitchMessage
}

/** Authentication policy of this controller. [secret] null/empty means unsigned mode (only if [allowUnsigned]). */
data class AuthConfig(val secret: String?, val allowUnsigned: Boolean) {
    val hasSecret: Boolean get() = !secret.isNullOrEmpty()
    val canSend: Boolean get() = hasSecret || allowUnsigned
}

object DemoSwitchProtocol {
    const val PORT = 7710
    const val MAX_PAYLOAD_BYTES = 1024
    const val MAX_SEQ = 9007199254740991L
    const val MAX_STATUS_MESSAGE_BYTES = 256
    /** FAILED / not_allowed message of a runtime that is running but not in the foreground. */
    const val NOT_IN_FOREGROUND_MESSAGE = "not in foreground"
    /** demo_id of PRESET_SET / PRESET_START: only the Hub handles Hub presets. */
    const val HUB_DEMO_ID = "demo_hub"
    const val MAX_PRESET_STEPS = 32
    const val MAX_PRESET_OPTIONS = 8
    val PRESET_NUMBERS = 1..3
    /** Range of PRESET_GET / PRESET `from`. */
    val PRESET_FROM = 0..31
    /** Installed demos of HUB_SETTINGS (`demo_count`, `demos`, `visible_demos`) and the range of its `from`. */
    const val MAX_HUB_DEMOS = 64
    val HUB_SETTINGS_FROM = 0..63
    const val MAX_DEVICE_MODEL_CODE_POINTS = 64
    val HAND_STYLES = setOf("ghost", "skin")
    /** STATE `screen`: the completion panel / the Hub's finish screen is `completion`, the Hub's manage screen `manage`. */
    val SCREENS = setOf("main", "completion", "manage")

    val STATUS_TYPES = setOf("ACK", "READY", "FAILED")
    val STATUS_CODES = setOf(
        "ok", "invalid_payload", "unsupported_version", "invalid_auth", "unsigned_disabled",
        "not_allowed", "replay", "launch_failed", "listener_failed",
    )
    val CONTROL_ACTIONS = setOf(
        "menu_open", "menu_close", "recenter", "restart", "scene", "haptics_on", "haptics_off",
        "haptics_ui_show", "haptics_ui_hide", "recenter_ui_show", "recenter_ui_hide", "tutorial_start",
        "hand_style_ghost", "hand_style_skin", "session_next", "session_retry", "hub_top", "hub_replay",
    )

    private const val HEADER = "HAPBEAT-DEMO-SWITCH/1\n"
    private val IDENTIFIER = Regex("^[a-z0-9][a-z0-9._-]{0,63}$")
    private val NONCE = Regex("^[0-9a-f]{16}$")
    private val AUTH = Regex("^[0-9a-f]{64}$")
    private val OPTION_VALUE = Regex("^[a-z0-9][a-z0-9._-]{0,31}$")
    private val random = SecureRandom()

    fun isIdentifier(value: String): Boolean = IDENTIFIER.matches(value)

    fun newNonce(rng: SecureRandom = random): String = hex(ByteArray(8).also { rng.nextBytes(it) })

    // ---- canonical bytes ----------------------------------------------------------------

    private fun field(sb: StringBuilder, name: String, value: String) {
        sb.append(name).append('=').append(value.toByteArray(Charsets.UTF_8).size).append(':').append(value).append('\n')
    }

    fun canonicalCommand(controllerId: String, seq: Long, demoId: String): String = StringBuilder(HEADER + "COMMAND\n").apply {
        field(this, "version", "1"); field(this, "type", "SWITCH"); field(this, "controller_id", controllerId)
        field(this, "seq", seq.toString()); field(this, "demo_id", demoId)
    }.toString()

    fun canonicalControl(controllerId: String, seq: Long, demoId: String, action: String, sceneId: String): String =
        StringBuilder(HEADER + "COMMAND\n").apply {
            field(this, "version", "1"); field(this, "type", "CONTROL"); field(this, "controller_id", controllerId)
            field(this, "seq", seq.toString()); field(this, "demo_id", demoId)
            field(this, "action", action); field(this, "scene_id", sceneId)
        }.toString()

    fun canonicalStatus(
        type: String, controllerId: String, seq: Long, demoId: String, currentDemoId: String, code: String, message: String,
    ): String = StringBuilder(HEADER + "STATUS\n").apply {
        field(this, "version", "1"); field(this, "type", type); field(this, "controller_id", controllerId)
        field(this, "seq", seq.toString()); field(this, "demo_id", demoId); field(this, "current_demo_id", currentDemoId)
        field(this, "code", code); field(this, "message", message)
    }.toString()

    fun canonicalDiscover(controllerId: String, nonce: String): String = StringBuilder(HEADER + "DISCOVER\n").apply {
        field(this, "version", "1"); field(this, "type", "DISCOVER"); field(this, "controller_id", controllerId); field(this, "nonce", nonce)
    }.toString()

    fun canonicalHere(controllerId: String, nonce: String, currentDemoId: String): String = StringBuilder(HEADER + "HERE\n").apply {
        field(this, "version", "1"); field(this, "type", "HERE"); field(this, "controller_id", controllerId)
        field(this, "nonce", nonce); field(this, "current_demo_id", currentDemoId)
    }.toString()

    fun canonicalQuery(controllerId: String, nonce: String): String = StringBuilder(HEADER + "QUERY\n").apply {
        field(this, "version", "1"); field(this, "type", "QUERY"); field(this, "controller_id", controllerId); field(this, "nonce", nonce)
    }.toString()

    fun canonicalState(m: DemoSwitchMessage.State): String = StringBuilder(HEADER + "STATE\n").apply {
        field(this, "version", "1"); field(this, "type", "STATE"); field(this, "controller_id", m.controllerId)
        field(this, "nonce", m.nonce); field(this, "current_demo_id", m.currentDemoId)
        field(this, "foreground", m.foreground.toString())
        field(this, "haptics_on", m.hapticsOn.toString()); field(this, "haptics_ui", m.hapticsUi.toString())
        field(this, "recenter_ui", m.recenterUi.toString()); field(this, "paused", m.paused.toString())
        field(this, "step_index", m.stepIndex.toString()); field(this, "step_count", m.stepCount.toString())
        // The optional fields, in this order, only when present.
        m.deviceModel?.let { field(this, "device_model", it) }
        m.editor?.let { field(this, "editor", it.toString()) }
        m.screen?.let { field(this, "screen", it) }
        m.handStyle?.let { field(this, "hand_style", it) }
    }.toString()

    fun canonicalPresetGet(controllerId: String, nonce: String, preset: Int, from: Int): String =
        StringBuilder(HEADER + "PRESET_GET\n").apply {
            field(this, "version", "1"); field(this, "type", "PRESET_GET"); field(this, "controller_id", controllerId)
            field(this, "nonce", nonce); field(this, "preset", preset.toString()); field(this, "from", from.toString())
        }.toString()

    fun canonicalPreset(m: DemoSwitchMessage.Preset): String = StringBuilder(HEADER + "PRESET\n").apply {
        field(this, "version", "1"); field(this, "type", "PRESET"); field(this, "controller_id", m.controllerId)
        field(this, "nonce", m.nonce); field(this, "preset", m.preset.toString()); field(this, "revision", m.revision.toString())
        field(this, "name", m.name); field(this, "visible", m.visible.toString()); field(this, "step_count", m.stepCount.toString())
        field(this, "from", m.from.toString()); field(this, "steps", canonicalSteps(m.steps))
    }.toString()

    fun canonicalPresetSet(controllerId: String, seq: Long, preset: Int, name: String, visible: Boolean, steps: List<PresetStep>): String =
        StringBuilder(HEADER + "COMMAND\n").apply {
            field(this, "version", "1"); field(this, "type", "PRESET_SET"); field(this, "controller_id", controllerId)
            field(this, "seq", seq.toString()); field(this, "demo_id", HUB_DEMO_ID); field(this, "preset", preset.toString())
            field(this, "name", name); field(this, "visible", visible.toString()); field(this, "steps", canonicalSteps(steps))
        }.toString()

    fun canonicalPresetStart(controllerId: String, seq: Long, preset: Int): String = StringBuilder(HEADER + "COMMAND\n").apply {
        field(this, "version", "1"); field(this, "type", "PRESET_START"); field(this, "controller_id", controllerId)
        field(this, "seq", seq.toString()); field(this, "demo_id", HUB_DEMO_ID); field(this, "preset", preset.toString())
    }.toString()

    fun canonicalHubSettingsGet(controllerId: String, nonce: String, from: Int): String =
        StringBuilder(HEADER + "HUB_SETTINGS_GET\n").apply {
            field(this, "version", "1"); field(this, "type", "HUB_SETTINGS_GET"); field(this, "controller_id", controllerId)
            field(this, "nonce", nonce); field(this, "from", from.toString())
        }.toString()

    fun canonicalHubSettings(m: DemoSwitchMessage.HubSettings): String = StringBuilder(HEADER + "HUB_SETTINGS\n").apply {
        field(this, "version", "1"); field(this, "type", "HUB_SETTINGS"); field(this, "controller_id", m.controllerId)
        field(this, "nonce", m.nonce); field(this, "revision", m.revision.toString())
        field(this, "haptics_ui", m.hapticsUi.toString()); field(this, "recenter_ui", m.recenterUi.toString())
        field(this, "hand_style", m.handStyle); field(this, "staff_waiting", m.staffWaiting.toString())
        field(this, "player", m.player.toString()); field(this, "group", m.group.toString())
        field(this, "demo_count", m.demoCount.toString()); field(this, "from", m.from.toString())
        field(this, "demos", canonicalHubDemos(m.demos))
    }.toString()

    fun canonicalHubSettingsSet(
        controllerId: String, seq: Long, hapticsUi: Boolean, recenterUi: Boolean, handStyle: String, staffWaiting: Boolean,
        visibleDemos: List<String>,
    ): String = StringBuilder(HEADER + "COMMAND\n").apply {
        field(this, "version", "1"); field(this, "type", "HUB_SETTINGS_SET"); field(this, "controller_id", controllerId)
        field(this, "seq", seq.toString()); field(this, "demo_id", HUB_DEMO_ID)
        field(this, "haptics_ui", hapticsUi.toString()); field(this, "recenter_ui", recenterUi.toString())
        field(this, "hand_style", handStyle); field(this, "staff_waiting", staffWaiting.toString())
        field(this, "visible_demos", visibleDemos.joinToString("|"))
    }.toString()

    fun canonicalHubStart(controllerId: String, seq: Long, steps: List<PresetStep>): String = StringBuilder(HEADER + "COMMAND\n").apply {
        field(this, "version", "1"); field(this, "type", "HUB_START"); field(this, "controller_id", controllerId)
        field(this, "seq", seq.toString()); field(this, "demo_id", HUB_DEMO_ID); field(this, "steps", canonicalSteps(steps))
    }.toString()

    /**
     * Signed value of HUB_SETTINGS `demos`: `<demo_id>;<1|0>;<N>:<title>` joined with `|`, where N is the title's UTF-8
     * byte count (a title may contain any character, so it is length-prefixed).
     */
    fun canonicalHubDemos(demos: List<HubDemo>): String = demos.joinToString("|") { demo ->
        "${demo.demoId};${if (demo.visible) 1 else 0};${demo.title.toByteArray(Charsets.UTF_8).size}:${demo.title}"
    }

    /**
     * Signed value of `steps`: `<demo_id>;<key=value,... sorted by key>;<1|0>` joined with `|` (retry omitted = 1).
     * Identifiers and option values cannot contain `|`, `;`, `,` or `=`, so this is unambiguous.
     */
    fun canonicalSteps(steps: List<PresetStep>): String = steps.joinToString("|") { step ->
        val options = step.options.entries.sortedBy { it.key }.joinToString(",") { "${it.key}=${it.value}" }
        "${step.demoId};$options;${if (step.retry) 1 else 0}"
    }

    fun canonical(message: DemoSwitchMessage): String = when (message) {
        is DemoSwitchMessage.Switch -> canonicalCommand(message.controllerId, message.seq, message.demoId)
        is DemoSwitchMessage.Control -> canonicalControl(message.controllerId, message.seq, message.demoId, message.action, message.sceneId)
        is DemoSwitchMessage.Status -> canonicalStatus(
            message.type, message.controllerId, message.seq, message.demoId, message.currentDemoId, message.code, message.message,
        )
        is DemoSwitchMessage.Discover -> canonicalDiscover(message.controllerId, message.nonce)
        is DemoSwitchMessage.Here -> canonicalHere(message.controllerId, message.nonce, message.currentDemoId)
        is DemoSwitchMessage.Query -> canonicalQuery(message.controllerId, message.nonce)
        is DemoSwitchMessage.State -> canonicalState(message)
        is DemoSwitchMessage.PresetGet -> canonicalPresetGet(message.controllerId, message.nonce, message.preset, message.from)
        is DemoSwitchMessage.Preset -> canonicalPreset(message)
        is DemoSwitchMessage.PresetSet ->
            canonicalPresetSet(message.controllerId, message.seq, message.preset, message.name, message.visible, message.steps)
        is DemoSwitchMessage.PresetStart -> canonicalPresetStart(message.controllerId, message.seq, message.preset)
        is DemoSwitchMessage.HubSettingsGet -> canonicalHubSettingsGet(message.controllerId, message.nonce, message.from)
        is DemoSwitchMessage.HubSettings -> canonicalHubSettings(message)
        is DemoSwitchMessage.HubSettingsSet -> canonicalHubSettingsSet(
            message.controllerId, message.seq, message.hapticsUi, message.recenterUi, message.handStyle, message.staffWaiting,
            message.visibleDemos,
        )
        is DemoSwitchMessage.HubStart -> canonicalHubStart(message.controllerId, message.seq, message.steps)
    }

    fun hmacHex(secret: String, canonical: String): String {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(secret.toByteArray(Charsets.UTF_8), "HmacSHA256"))
        return hex(mac.doFinal(canonical.toByteArray(Charsets.UTF_8)))
    }

    /** Applies the controller's auth rule to a received message (timing-safe comparison). */
    fun authAccepted(message: DemoSwitchMessage, config: AuthConfig): Boolean {
        val auth = message.auth
        if (config.hasSecret) {
            if (auth == null || !AUTH.matches(auth)) return false
            val expected = hmacHex(config.secret!!, canonical(message))
            return MessageDigest.isEqual(expected.toByteArray(Charsets.US_ASCII), auth.toByteArray(Charsets.US_ASCII))
        }
        return config.allowUnsigned && auth == null
    }

    // ---- outgoing messages (field order follows the spec examples) ------------------------

    private fun withAuth(fields: LinkedHashMap<String, Any?>, config: AuthConfig, canonical: String): String {
        if (config.hasSecret) fields["auth"] = hmacHex(config.secret!!, canonical)
        return MiniJson.write(fields)
    }

    fun buildSwitch(controllerId: String, seq: Long, demoId: String, config: AuthConfig): String = withAuth(
        linkedMapOf("version" to 1, "type" to "SWITCH", "controller_id" to controllerId, "seq" to seq, "demo_id" to demoId),
        config, canonicalCommand(controllerId, seq, demoId),
    )

    fun buildControl(controllerId: String, seq: Long, demoId: String, action: String, sceneId: String, config: AuthConfig): String = withAuth(
        linkedMapOf(
            "version" to 1, "type" to "CONTROL", "controller_id" to controllerId, "seq" to seq, "demo_id" to demoId,
            "action" to action, "scene_id" to sceneId,
        ),
        config, canonicalControl(controllerId, seq, demoId, action, sceneId),
    )

    fun buildDiscover(controllerId: String, nonce: String, config: AuthConfig): String = withAuth(
        linkedMapOf("version" to 1, "type" to "DISCOVER", "controller_id" to controllerId, "nonce" to nonce),
        config, canonicalDiscover(controllerId, nonce),
    )

    fun buildQuery(controllerId: String, nonce: String, config: AuthConfig): String = withAuth(
        linkedMapOf("version" to 1, "type" to "QUERY", "controller_id" to controllerId, "nonce" to nonce),
        config, canonicalQuery(controllerId, nonce),
    )

    fun buildPresetGet(controllerId: String, nonce: String, preset: Int, from: Int, config: AuthConfig): String = withAuth(
        linkedMapOf("version" to 1, "type" to "PRESET_GET", "controller_id" to controllerId, "nonce" to nonce, "preset" to preset, "from" to from),
        config, canonicalPresetGet(controllerId, nonce, preset, from),
    )

    /**
     * PRESET_SET with [steps] as given (callers pass validated steps, see HubPresets). `options` is left out when
     * empty and `retry` when true, which signs the same as writing them.
     */
    fun buildPresetSet(
        controllerId: String, seq: Long, preset: Int, name: String, visible: Boolean, steps: List<PresetStep>, config: AuthConfig,
    ): String = withAuth(
        linkedMapOf(
            "version" to 1, "type" to "PRESET_SET", "controller_id" to controllerId, "seq" to seq, "demo_id" to HUB_DEMO_ID,
            "preset" to preset, "name" to name, "visible" to visible, "steps" to stepsJson(steps),
        ),
        config, canonicalPresetSet(controllerId, seq, preset, name, visible, steps),
    )

    fun buildPresetStart(controllerId: String, seq: Long, preset: Int, config: AuthConfig): String = withAuth(
        linkedMapOf("version" to 1, "type" to "PRESET_START", "controller_id" to controllerId, "seq" to seq, "demo_id" to HUB_DEMO_ID, "preset" to preset),
        config, canonicalPresetStart(controllerId, seq, preset),
    )

    fun buildHubSettingsGet(controllerId: String, nonce: String, from: Int, config: AuthConfig): String = withAuth(
        linkedMapOf("version" to 1, "type" to "HUB_SETTINGS_GET", "controller_id" to controllerId, "nonce" to nonce, "from" to from),
        config, canonicalHubSettingsGet(controllerId, nonce, from),
    )

    fun buildHubSettingsSet(
        controllerId: String, seq: Long, hapticsUi: Boolean, recenterUi: Boolean, handStyle: String, staffWaiting: Boolean,
        visibleDemos: List<String>, config: AuthConfig,
    ): String = withAuth(
        linkedMapOf(
            "version" to 1, "type" to "HUB_SETTINGS_SET", "controller_id" to controllerId, "seq" to seq, "demo_id" to HUB_DEMO_ID,
            "haptics_ui" to hapticsUi, "recenter_ui" to recenterUi, "hand_style" to handStyle, "staff_waiting" to staffWaiting,
            "visible_demos" to visibleDemos,
        ),
        config, canonicalHubSettingsSet(controllerId, seq, hapticsUi, recenterUi, handStyle, staffWaiting, visibleDemos),
    )

    /** HUB_START with [steps] as given (callers pass validated steps); `options` / `retry` are written as in PRESET_SET. */
    fun buildHubStart(controllerId: String, seq: Long, steps: List<PresetStep>, config: AuthConfig): String = withAuth(
        linkedMapOf(
            "version" to 1, "type" to "HUB_START", "controller_id" to controllerId, "seq" to seq, "demo_id" to HUB_DEMO_ID,
            "steps" to stepsJson(steps),
        ),
        config, canonicalHubStart(controllerId, seq, steps),
    )

    /** `steps` of PRESET_SET / HUB_START: `options` left out when empty and `retry` when true (signs the same). */
    private fun stepsJson(steps: List<PresetStep>): List<Map<String, Any?>> = steps.map { step ->
        linkedMapOf<String, Any?>("demo_id" to step.demoId).apply {
            if (step.options.isNotEmpty()) put("options", LinkedHashMap(step.options))
            if (!step.retry) put("retry", false)
        }
    }

    /**
     * Bytes of the PRESET_SET for these values in the worst case: the largest sequence and an `auth` field, so a plan
     * that fits here fits whatever the next sequence and the authentication mode.
     */
    fun presetSetWorstCaseBytes(controllerId: String, preset: Int, name: String, visible: Boolean, steps: List<PresetStep>): Int =
        buildPresetSet(controllerId, MAX_SEQ, preset, name, visible, steps, WORST_CASE_AUTH).toByteArray(Charsets.UTF_8).size

    /** Any secret: the auth field is always 64 hex characters. */
    private val WORST_CASE_AUTH = AuthConfig(secret = "worst-case", allowUnsigned = false)

    // ---- parsing / schema validation ---------------------------------------------------------

    /** Parses one UDP payload. Returns null for anything that does not match the v1 schema. */
    fun parse(payload: ByteArray): DemoSwitchMessage? {
        if (payload.isEmpty() || payload.size > MAX_PAYLOAD_BYTES) return null
        val text = decodeUtf8Strict(payload) ?: return null
        val root = try { MiniJson.parse(text) } catch (_: JsonException) { return null }
        val obj = (root as? JsonObject)?.fields ?: return null
        if (intField(obj, "version") != 1L) return null
        val auth = if (obj.containsKey("auth")) (stringField(obj, "auth") ?: return null).also { if (!AUTH.matches(it)) return null } else null
        val controllerId = identifierField(obj, "controller_id") ?: return null
        return when (stringField(obj, "type")) {
            "SWITCH" -> {
                if (!onlyFields(obj, SWITCH_FIELDS)) return null
                DemoSwitchMessage.Switch(controllerId, seqField(obj) ?: return null, identifierField(obj, "demo_id") ?: return null, auth)
            }
            "CONTROL" -> {
                if (!onlyFields(obj, CONTROL_FIELDS)) return null
                val action = stringField(obj, "action")?.takeIf { it in CONTROL_ACTIONS } ?: return null
                val sceneId = stringField(obj, "scene_id") ?: return null
                if (action == "scene" && !isIdentifier(sceneId)) return null
                if (action != "scene" && sceneId.isNotEmpty()) return null
                DemoSwitchMessage.Control(
                    controllerId, seqField(obj) ?: return null, identifierField(obj, "demo_id") ?: return null, action, sceneId, auth,
                )
            }
            "ACK", "READY", "FAILED" -> {
                if (!onlyFields(obj, STATUS_FIELDS)) return null
                val message = stringField(obj, "message") ?: return null
                if (message.toByteArray(Charsets.UTF_8).size > MAX_STATUS_MESSAGE_BYTES) return null
                DemoSwitchMessage.Status(
                    type = stringField(obj, "type")!!,
                    controllerId = controllerId,
                    seq = seqField(obj) ?: return null,
                    demoId = identifierField(obj, "demo_id") ?: return null,
                    currentDemoId = identifierField(obj, "current_demo_id") ?: return null,
                    code = stringField(obj, "code")?.takeIf { it in STATUS_CODES } ?: return null,
                    message = message,
                    auth = auth,
                )
            }
            "DISCOVER" -> {
                if (!onlyFields(obj, DISCOVER_FIELDS)) return null
                DemoSwitchMessage.Discover(controllerId, nonceField(obj) ?: return null, auth)
            }
            "HERE" -> {
                if (!onlyFields(obj, HERE_FIELDS)) return null
                DemoSwitchMessage.Here(controllerId, nonceField(obj) ?: return null, identifierField(obj, "current_demo_id") ?: return null, auth)
            }
            "QUERY" -> {
                if (!onlyFields(obj, DISCOVER_FIELDS)) return null
                DemoSwitchMessage.Query(controllerId, nonceField(obj) ?: return null, auth)
            }
            "STATE" -> {
                if (!onlyFields(obj, STATE_FIELDS, STATE_OPTIONAL_FIELDS)) return null
                DemoSwitchMessage.State(
                    controllerId = controllerId,
                    nonce = nonceField(obj) ?: return null,
                    currentDemoId = identifierField(obj, "current_demo_id") ?: return null,
                    foreground = boolField(obj, "foreground") ?: return null,
                    hapticsOn = boolField(obj, "haptics_on") ?: return null,
                    hapticsUi = boolField(obj, "haptics_ui") ?: return null,
                    recenterUi = boolField(obj, "recenter_ui") ?: return null,
                    paused = boolField(obj, "paused") ?: return null,
                    stepIndex = intField(obj, "step_index")?.takeIf { it in -1..32 } ?: return null,
                    stepCount = intField(obj, "step_count")?.takeIf { it in 0..32 } ?: return null,
                    auth = auth,
                    deviceModel = if (obj.containsKey("device_model")) {
                        stringField(obj, "device_model")?.takeIf { isDeviceModel(it) } ?: return null
                    } else null,
                    editor = if (obj.containsKey("editor")) boolField(obj, "editor") ?: return null else null,
                    screen = if (obj.containsKey("screen")) stringField(obj, "screen")?.takeIf { it in SCREENS } ?: return null else null,
                    handStyle = if (obj.containsKey("hand_style")) handStyleField(obj) ?: return null else null,
                )
            }
            "PRESET_GET" -> {
                if (!onlyFields(obj, PRESET_GET_FIELDS)) return null
                DemoSwitchMessage.PresetGet(
                    controllerId, nonceField(obj) ?: return null, presetField(obj) ?: return null, fromField(obj) ?: return null, auth,
                )
            }
            "PRESET" -> {
                if (!onlyFields(obj, PRESET_FIELDS)) return null
                DemoSwitchMessage.Preset(
                    controllerId = controllerId,
                    nonce = nonceField(obj) ?: return null,
                    preset = presetField(obj) ?: return null,
                    revision = intField(obj, "revision")?.takeIf { it in 0..MAX_SEQ } ?: return null,
                    name = presetNameField(obj) ?: return null,
                    visible = boolField(obj, "visible") ?: return null,
                    stepCount = intField(obj, "step_count")?.takeIf { it in 0..MAX_PRESET_STEPS }?.toInt() ?: return null,
                    from = fromField(obj) ?: return null,
                    steps = presetStepsField(obj) ?: return null,
                    auth = auth,
                )
            }
            "PRESET_SET" -> {
                if (!onlyFields(obj, PRESET_SET_FIELDS)) return null
                DemoSwitchMessage.PresetSet(
                    controllerId = controllerId,
                    seq = seqField(obj) ?: return null,
                    demoId = stringField(obj, "demo_id")?.takeIf { it == HUB_DEMO_ID } ?: return null,
                    preset = presetField(obj) ?: return null,
                    name = presetNameField(obj) ?: return null,
                    visible = boolField(obj, "visible") ?: return null,
                    steps = presetStepsField(obj) ?: return null,
                    auth = auth,
                )
            }
            "PRESET_START" -> {
                if (!onlyFields(obj, PRESET_START_FIELDS)) return null
                DemoSwitchMessage.PresetStart(
                    controllerId, seqField(obj) ?: return null, stringField(obj, "demo_id")?.takeIf { it == HUB_DEMO_ID } ?: return null,
                    presetField(obj) ?: return null, auth,
                )
            }
            "HUB_SETTINGS_GET" -> {
                if (!onlyFields(obj, HUB_SETTINGS_GET_FIELDS)) return null
                DemoSwitchMessage.HubSettingsGet(controllerId, nonceField(obj) ?: return null, hubFromField(obj) ?: return null, auth)
            }
            "HUB_SETTINGS" -> {
                if (!onlyFields(obj, HUB_SETTINGS_FIELDS)) return null
                DemoSwitchMessage.HubSettings(
                    controllerId = controllerId,
                    nonce = nonceField(obj) ?: return null,
                    revision = intField(obj, "revision")?.takeIf { it in 0..MAX_SEQ } ?: return null,
                    hapticsUi = boolField(obj, "haptics_ui") ?: return null,
                    recenterUi = boolField(obj, "recenter_ui") ?: return null,
                    handStyle = handStyleField(obj) ?: return null,
                    staffWaiting = boolField(obj, "staff_waiting") ?: return null,
                    player = deviceAxisField(obj, "player") ?: return null,
                    group = deviceAxisField(obj, "group") ?: return null,
                    demoCount = intField(obj, "demo_count")?.takeIf { it in 0..MAX_HUB_DEMOS }?.toInt() ?: return null,
                    from = hubFromField(obj) ?: return null,
                    demos = hubDemosField(obj) ?: return null,
                    auth = auth,
                )
            }
            "HUB_SETTINGS_SET" -> {
                if (!onlyFields(obj, HUB_SETTINGS_SET_FIELDS)) return null
                val visible = (obj["visible_demos"] as? JsonArray)?.items ?: return null
                if (visible.size > MAX_HUB_DEMOS) return null
                val ids = visible.map { (it as? JsonString)?.value?.takeIf { id -> isIdentifier(id) } ?: return null }
                if (ids.toSet().size != ids.size) return null
                DemoSwitchMessage.HubSettingsSet(
                    controllerId = controllerId,
                    seq = seqField(obj) ?: return null,
                    demoId = stringField(obj, "demo_id")?.takeIf { it == HUB_DEMO_ID } ?: return null,
                    hapticsUi = boolField(obj, "haptics_ui") ?: return null,
                    recenterUi = boolField(obj, "recenter_ui") ?: return null,
                    handStyle = handStyleField(obj) ?: return null,
                    staffWaiting = boolField(obj, "staff_waiting") ?: return null,
                    visibleDemos = ids,
                    auth = auth,
                )
            }
            "HUB_START" -> {
                if (!onlyFields(obj, HUB_START_FIELDS)) return null
                DemoSwitchMessage.HubStart(
                    controllerId, seqField(obj) ?: return null, stringField(obj, "demo_id")?.takeIf { it == HUB_DEMO_ID } ?: return null,
                    presetStepsField(obj)?.takeIf { it.isNotEmpty() } ?: return null, auth,
                )
            }
            else -> null
        }
    }

    /** Hub preset name: "" or the remote preset transfer name rules (1..40 code points, demo-session.md). */
    fun isPresetName(name: String): Boolean = name.isEmpty() || PresetTransfer.isValidName(name)

    private fun presetField(obj: Map<String, JsonValue>): Int? =
        intField(obj, "preset")?.takeIf { it in PRESET_NUMBERS.first..PRESET_NUMBERS.last }?.toInt()

    private fun fromField(obj: Map<String, JsonValue>): Int? =
        intField(obj, "from")?.takeIf { it in PRESET_FROM.first..PRESET_FROM.last }?.toInt()

    private fun presetNameField(obj: Map<String, JsonValue>): String? = stringField(obj, "name")?.takeIf { isPresetName(it) }

    /** STATE `device_model`: 1..64 code points without C0 / C1 control characters (U+0000..U+001F, U+007F..U+009F). */
    fun isDeviceModel(value: String): Boolean =
        value.codePointCount(0, value.length) in 1..MAX_DEVICE_MODEL_CODE_POINTS && value.none { it in '\u0000'..'\u001f' || it in '\u007f'..'\u009f' }

    private fun handStyleField(obj: Map<String, JsonValue>): String? = stringField(obj, "hand_style")?.takeIf { it in HAND_STYLES }

    private fun hubFromField(obj: Map<String, JsonValue>): Int? =
        intField(obj, "from")?.takeIf { it in HUB_SETTINGS_FROM.first..HUB_SETTINGS_FROM.last }?.toInt()

    /** Device address axis: 1..99, or -1 when not specified. */
    private fun deviceAxisField(obj: Map<String, JsonValue>, name: String): Int? =
        intField(obj, name)?.takeIf { it == -1L || it in 1..99 }?.toInt()

    /** HUB_SETTINGS `demos` (0..64): each exactly `demo_id`, `title` (the preset name rules), `visible`. */
    private fun hubDemosField(obj: Map<String, JsonValue>): List<HubDemo>? {
        val items = (obj["demos"] as? JsonArray)?.items ?: return null
        if (items.size > MAX_HUB_DEMOS) return null
        return items.map { item ->
            val f = (item as? JsonObject)?.fields ?: return null
            if (f.keys != setOf("demo_id", "title", "visible")) return null
            HubDemo(
                identifierField(f, "demo_id") ?: return null,
                stringField(f, "title")?.takeIf { PresetTransfer.isValidName(it) } ?: return null,
                boolField(f, "visible") ?: return null,
            )
        }
    }

    /** `steps` (0..32): each `demo_id`, optional `options` (<= 8, identifier keys, option-value values), optional `retry`. */
    private fun presetStepsField(obj: Map<String, JsonValue>): List<PresetStep>? {
        val items = (obj["steps"] as? JsonArray)?.items ?: return null
        if (items.size > MAX_PRESET_STEPS) return null
        return items.map { item ->
            val f = (item as? JsonObject)?.fields ?: return null
            if (!f.keys.all { it == "demo_id" || it == "options" || it == "retry" }) return null
            val demoId = identifierField(f, "demo_id") ?: return null
            val options = when (val o = f["options"]) {
                null -> emptyMap()
                is JsonObject -> {
                    if (o.fields.size > MAX_PRESET_OPTIONS) return null
                    o.fields.mapValues { (key, v) ->
                        val value = (v as? JsonString)?.value ?: return null
                        if (!isIdentifier(key) || !OPTION_VALUE.matches(value)) return null
                        value
                    }
                }
                else -> return null
            }
            val retry = when (val r = f["retry"]) {
                null -> true
                is JsonBool -> r.value
                else -> return null
            }
            PresetStep(demoId, options, retry)
        }
    }

    private val SWITCH_FIELDS = setOf("version", "type", "controller_id", "seq", "demo_id")
    private val CONTROL_FIELDS = SWITCH_FIELDS + setOf("action", "scene_id")
    private val STATUS_FIELDS = SWITCH_FIELDS + setOf("current_demo_id", "code", "message")
    private val DISCOVER_FIELDS = setOf("version", "type", "controller_id", "nonce")
    private val HERE_FIELDS = DISCOVER_FIELDS + "current_demo_id"
    private val STATE_FIELDS = HERE_FIELDS + setOf("foreground", "haptics_on", "haptics_ui", "recenter_ui", "paused", "step_index", "step_count")
    private val PRESET_GET_FIELDS = DISCOVER_FIELDS + setOf("preset", "from")
    private val PRESET_FIELDS = PRESET_GET_FIELDS + setOf("revision", "name", "visible", "step_count", "steps")
    private val PRESET_START_FIELDS = SWITCH_FIELDS + "preset"
    private val PRESET_SET_FIELDS = PRESET_START_FIELDS + setOf("name", "visible", "steps")
    private val STATE_OPTIONAL_FIELDS = setOf("device_model", "editor", "screen", "hand_style")
    private val HUB_SETTINGS_GET_FIELDS = DISCOVER_FIELDS + "from"
    private val HUB_SETTINGS_FIELDS = HUB_SETTINGS_GET_FIELDS + setOf(
        "revision", "haptics_ui", "recenter_ui", "hand_style", "staff_waiting", "player", "group", "demo_count", "demos",
    )
    private val HUB_SETTINGS_SET_FIELDS = SWITCH_FIELDS + setOf("haptics_ui", "recenter_ui", "hand_style", "staff_waiting", "visible_demos")
    private val HUB_START_FIELDS = SWITCH_FIELDS + "steps"

    /** Required fields present and no field other than those, [optional] ones and auth. */
    private fun onlyFields(obj: Map<String, JsonValue>, required: Set<String>, optional: Set<String> = emptySet()): Boolean =
        obj.keys.containsAll(required) && obj.keys.all { it in required || it in optional || it == "auth" }

    private fun stringField(obj: Map<String, JsonValue>, name: String): String? = (obj[name] as? JsonString)?.value
    private fun intField(obj: Map<String, JsonValue>, name: String): Long? = (obj[name] as? JsonNumber)?.longOrNull()
    private fun boolField(obj: Map<String, JsonValue>, name: String): Boolean? = (obj[name] as? JsonBool)?.value
    private fun identifierField(obj: Map<String, JsonValue>, name: String): String? = stringField(obj, name)?.takeIf { isIdentifier(it) }
    private fun nonceField(obj: Map<String, JsonValue>): String? = stringField(obj, "nonce")?.takeIf { NONCE.matches(it) }
    private fun seqField(obj: Map<String, JsonValue>): Long? = intField(obj, "seq")?.takeIf { it in 1..MAX_SEQ }

    private fun decodeUtf8Strict(bytes: ByteArray): String? = try {
        Charsets.UTF_8.newDecoder().decode(java.nio.ByteBuffer.wrap(bytes)).toString()
    } catch (_: java.nio.charset.CharacterCodingException) {
        null
    }

    private fun hex(bytes: ByteArray): String = buildString(bytes.size * 2) {
        for (b in bytes) { append(HEX[(b.toInt() shr 4) and 0xf]); append(HEX[b.toInt() and 0xf]) }
    }

    private const val HEX = "0123456789abcdef"

    // ---- receive-side checks (M5 controller validHere / validStatus) --------------------------

    /** IPv4 unicast host address (not 0/8, loopback, multicast, reserved or limited broadcast). */
    fun isUnicastIpv4(address: String): Boolean {
        val parts = address.split('.')
        if (parts.size != 4) return false
        val octets = parts.map { p -> p.toIntOrNull()?.takeIf { it in 0..255 && p == it.toString() } ?: return false }
        val first = octets[0]
        return first in 1..223 && first != 127
    }

    /**
     * HERE acceptance: unicast source, own controller ID, a nonce of a running round,
     * optional [expectedSource] (CONTROL pre-check binds to the selected Quest) and the auth rule.
     */
    fun acceptHere(
        message: DemoSwitchMessage.Here, source: String, controllerId: String, activeNonces: Set<String>,
        config: AuthConfig, expectedSource: String? = null,
    ): Boolean {
        if (!isUnicastIpv4(source)) return false
        if (expectedSource != null && source != expectedSource) return false
        if (message.controllerId != controllerId || message.nonce !in activeNonces) return false
        return authAccepted(message, config)
    }

    /** STATE acceptance: from the Quest the QUERY went to, own controller ID, that QUERY's nonce, auth rule. */
    fun acceptState(
        message: DemoSwitchMessage.State, source: String, controllerId: String, queryTarget: (nonce: String) -> String?,
        config: AuthConfig,
    ): Boolean {
        if (message.controllerId != controllerId) return false
        if (queryTarget(message.nonce) != source) return false
        return authAccepted(message, config)
    }

    /** PRESET acceptance: from the Quest the PRESET_GET went to, own controller ID, that request's nonce, auth rule. */
    fun acceptPreset(
        message: DemoSwitchMessage.Preset, source: String, controllerId: String, requestTarget: (nonce: String) -> String?,
        config: AuthConfig,
    ): Boolean {
        if (message.controllerId != controllerId) return false
        if (requestTarget(message.nonce) != source) return false
        return authAccepted(message, config)
    }

    /** HUB_SETTINGS acceptance: from the Quest the HUB_SETTINGS_GET went to, own controller ID, that request's nonce, auth rule. */
    fun acceptHubSettings(
        message: DemoSwitchMessage.HubSettings, source: String, controllerId: String, requestTarget: (nonce: String) -> String?,
        config: AuthConfig,
    ): Boolean {
        if (message.controllerId != controllerId) return false
        if (requestTarget(message.nonce) != source) return false
        return authAccepted(message, config)
    }

    /** Status acceptance: from the Quest the command was sent to, own controller ID, a pending (seq, demo_id). */
    fun acceptStatus(
        message: DemoSwitchMessage.Status, source: String, controllerId: String,
        pendingTarget: (seq: Long, demoId: String) -> String?, config: AuthConfig,
    ): Boolean {
        if (message.controllerId != controllerId) return false
        val target = pendingTarget(message.seq, message.demoId) ?: return false
        if (source != target) return false
        return authAccepted(message, config)
    }
}
