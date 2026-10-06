package com.hapbeat.demoremote

import com.hapbeat.demoremote.data.PresetStep
import com.hapbeat.demoremote.protocol.MiniJson

/** One value of a demo option ([label]: the descriptor's Japanese label). */
data class OptionValue(val value: String, val label: String)

/**
 * One option of a demo's descriptor (`options[]`): [id], Japanese [label], [default] and the allowed [values].
 * [whenValues] mirrors the descriptor's `when`: the option applies only while each referenced option has one of
 * the listed values (demo-session.md: otherwise it is left out of the ticket).
 */
data class DemoOption(
    val id: String, val label: String, val default: String, val values: List<OptionValue>,
    val whenValues: Map<String, Set<String>> = emptyMap(),
)

/**
 * Fixed demo table. adb launches only these packages; nothing received from the
 * network is ever used as a package name or shell command.
 * [options]: copy of the demo's descriptor options, the only option keys and values sent to the Hub.
 */
data class DemoApp(val demoId: String, val label: String, val packageName: String, val options: List<DemoOption> = emptyList())

object DemoCatalog {
    const val HUB_ID = "demo_hub"
    const val VOLLEY_ID = "volley"
    const val ENERGY_DUEL_ID = "energy-duel"

    // Descriptor options copied from each demo's hapbeat-demo-session.json (Unity: Assets/StreamingAssets,
    // Unreal: Config/HapbeatDemoSession). Update together with the descriptor.
    private val ENERGY_DUEL_OPTIONS = listOf(
        DemoOption("tutorial", "チュートリアル", "on", listOf(OptionValue("on", "あり"), OptionValue("off", "なし"))),
        DemoOption("round_seconds", "試合の長さ", "30", listOf(OptionValue("30", "30秒"), OptionValue("60", "60秒"))),
        DemoOption("difficulty", "相手の強さ", "normal", listOf(OptionValue("normal", "ふつう"), OptionValue("strong", "強い"))),
        DemoOption("mode", "モード", "match", listOf(OptionValue("match", "試合"), OptionValue("free", "フリープレイ"))),
    )
    private val VOLLEY_OPTIONS = listOf(
        DemoOption("scene", "モード", "block", listOf(
            OptionValue("block", "スパイク＋ブロック"), OptionValue("match", "6人制の試合"), OptionValue("receive", "レシーブ"),
        )),
        DemoOption("points", "点数", "7", listOf(OptionValue("3", "3点先取"), OptionValue("5", "5点先取"), OptionValue("7", "7点先取")),
            whenValues = mapOf("scene" to setOf("block", "match"))),
        DemoOption("balls", "球数", "10", listOf(OptionValue("10", "10球"), OptionValue("20", "20球")),
            whenValues = mapOf("scene" to setOf("receive"))),
    )
    private val BOXING_OPTIONS = listOf(
        DemoOption("round", "ラウンド", "90", listOf(OptionValue("60", "60秒"), OptionValue("90", "90秒"))),
    )

    val apps: List<DemoApp> = listOf(
        DemoApp(HUB_ID, "Demo Hub", "jp.hapbeat.demohub"),
        DemoApp(VOLLEY_ID, "Volley", "jp.hapbeat.volley", VOLLEY_OPTIONS),
        DemoApp("boxing", "Boxing", "com.hapbeat.boxing", BOXING_OPTIONS),
        DemoApp("handdemo", "Hand Demo", "com.Hapbeat.HapticHandDemo_G2"),
        DemoApp("trex-encounter", "T-Rex Encounter", "com.hapbeat.trexencounter"),
        DemoApp("safety-mill", "Safety Mill", "com.hapbeat.safetymill"),
        DemoApp(ENERGY_DUEL_ID, "Energy Duel", "jp.hapbeat.energyduel", ENERGY_DUEL_OPTIONS),
        DemoApp("fps", "FPS", "com.hapbeat.fpsdemo"),
    )

    val hub: DemoApp get() = apps.first { it.demoId == HUB_ID }

    /** Display name for a demo ID; unknown IDs are shown as-is. */
    fun labelFor(demoId: String?): String = apps.firstOrNull { it.demoId == demoId }?.label ?: (demoId ?: "-")

    /** Demo IDs that can start a one-demo Hub session (everything but the Hub itself). */
    val sessionApps: List<DemoApp> get() = apps.filter { it.demoId != HUB_ID }

    /** Descriptor options of a session demo (empty for unknown IDs). */
    fun optionsFor(demoId: String): List<DemoOption> = sessionApps.firstOrNull { it.demoId == demoId }?.options.orEmpty()

    /** True when [option]'s `when` holds for the chosen [values] (a missing key counts as its default). */
    fun isActive(demoId: String, option: DemoOption, values: Map<String, String>): Boolean =
        option.whenValues.all { (ref, allowed) ->
            val current = values[ref] ?: optionsFor(demoId).firstOrNull { it.id == ref }?.default
            current in allowed
        }

    /** Descriptor options that currently apply (see [DemoOption.whenValues]). */
    fun activeOptionsFor(demoId: String, values: Map<String, String>): List<DemoOption> =
        optionsFor(demoId).filter { isActive(demoId, it, values) }

    /** [values] without keys whose option does not apply; this is what goes to the Hub. */
    fun applicableOptions(demoId: String, values: Map<String, String>): Map<String, String> {
        val active = activeOptionsFor(demoId, values).map { it.id }.toSet()
        return values.filterKeys { it in active }
    }

    /** "チュートリアル: なし、モード: 試合" for the options that are set (empty when all are the descriptor default). */
    fun optionSummary(demoId: String, options: Map<String, String>): String = activeOptionsFor(demoId, options).mapNotNull { option ->
        val value = options[option.id] ?: return@mapNotNull null
        "${option.label}: ${option.values.firstOrNull { it.value == value }?.label ?: value}"
    }.joinToString("、")

    /** Identifier / option value patterns of demo-session.md (demo-remote-preset.schema.json). */
    val IDENTIFIER = Regex("^[a-z0-9][a-z0-9._-]{0,63}$")
    val OPTION_VALUE = Regex("^[a-z0-9][a-z0-9._-]{0,31}$")
    const val MAX_OPTIONS = 8

    /**
     * Throws IllegalArgumentException unless [demoId] is a session demo and every option key / value is in its
     * descriptor table. The patterns are checked first, so nothing outside `[a-z0-9._-]` gets further.
     */
    fun requireValidStep(demoId: String, options: Map<String, String>) {
        require(IDENTIFIER.matches(demoId))
        require(options.size <= MAX_OPTIONS)
        options.forEach { (key, value) -> require(IDENTIFIER.matches(key) && OPTION_VALUE.matches(value)) }
        require(sessionApps.any { it.demoId == demoId })
        val table = optionsFor(demoId)
        options.forEach { (key, value) -> require(table.any { o -> o.id == key && o.values.any { it.value == value } }) }
    }

    private const val HUB_COMPONENT = "jp.hapbeat.demohub/com.unity3d.player.UnityPlayerGameActivity"
    private const val HUB_START_EXTRA = "com.hapbeat.demo_hub.start"

    /**
     * Starts a Demo Session through the Hub's external start extra (demo-session.md「Hub を外部から起動して
     * セッションを始める」): a preset 1..3, or one demo with descriptor [options] (empty = all defaults).
     */
    fun hubSessionCommand(preset: Int? = null, demoId: String? = null, options: Map<String, String> = emptyMap()): String {
        val json = when {
            preset != null -> {
                require(preset in 1..3)
                MiniJson.write(linkedMapOf("version" to 1, "preset" to preset))
            }
            demoId != null -> {
                requireValidStep(demoId, options)
                val applicable = applicableOptions(demoId, options)
                MiniJson.write(linkedMapOf<String, Any?>("version" to 1, "demo_id" to demoId).apply {
                    if (applicable.isNotEmpty()) put("options", LinkedHashMap(applicable))
                })
            }
            else -> throw IllegalArgumentException("preset or demoId")
        }
        return hubStartCommand(json)
    }

    /** Hub start extra limit (demo-session.md). */
    const val HUB_START_MAX_BYTES = 4096

    /**
     * Starts a session with a plan built on this phone (`steps`, 1..32 demos). The JSON is rebuilt from the
     * validated steps (demo_id, options, retry); a preset's name never goes into it.
     */
    fun hubPlanCommand(steps: List<PresetStep>): String {
        require(steps.size in 1..32)
        val json = MiniJson.write(linkedMapOf(
            "version" to 1,
            "steps" to steps.map { step ->
                requireValidStep(step.demoId, step.options)
                val applicable = applicableOptions(step.demoId, step.options)
                linkedMapOf<String, Any?>("demo_id" to step.demoId).apply {
                    if (applicable.isNotEmpty()) put("options", LinkedHashMap(applicable))
                    if (!step.retry) put("retry", false)
                }
            },
        ))
        require(json.toByteArray(Charsets.UTF_8).size <= HUB_START_MAX_BYTES)
        return hubStartCommand(json)
    }

    private fun hubStartCommand(json: String): String = "am start -n $HUB_COMPONENT --es $HUB_START_EXTRA ${shellQuote(json)}"

    /**
     * One POSIX single-quoted shell word (' becomes '\''). Validated values cannot contain a quote; this is
     * defense in depth on top of the pattern check (demo-session.md).
     */
    fun shellQuote(value: String): String = "'" + value.replace("'", "'\\''") + "'"

    /** Shell command that resolves the launcher activity of [app] and starts it. */
    fun launchCommand(app: DemoApp): String =
        "c=\$(cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER " +
            "${app.packageName} | tail -n 1) && am start -n \"\$c\""
}

/** One CONTROL button: action + scene_id (empty except for scene). [demoId]: only for that foreground demo. */
data class ControlAction(val label: String, val action: String, val sceneId: String = "", val demoId: String? = null)

/** A titled row group on the 操作 tab. */
data class ControlGroup(val title: String, val actions: List<ControlAction>)

object ControlCatalog {
    val menuOpen = ControlAction("メニューを開く", "menu_open")

    val groups: List<ControlGroup> = listOf(
        ControlGroup("進行", listOf(
            ControlAction("チュートリアル開始", "tutorial_start"),
            ControlAction("最初から", "restart"),
            menuOpen,
            ControlAction("メニューを閉じる", "menu_close"),
        )),
        ControlGroup("視線・触覚", listOf(
            ControlAction("視線リセット", "recenter"),
            ControlAction("触覚 ON", "haptics_on"),
            ControlAction("触覚 OFF", "haptics_off"),
        )),
        ControlGroup("HMD 内のボタン表示", listOf(
            ControlAction("触覚ボタン 表示", "haptics_ui_show"),
            ControlAction("触覚ボタン 非表示", "haptics_ui_hide"),
            ControlAction("リセットボタン 表示", "recenter_ui_show"),
            ControlAction("リセットボタン 非表示", "recenter_ui_hide"),
        )),
        ControlGroup("Volley", listOf(
            // Same IDs as the Volley descriptor's scenes (block is its default).
            ControlAction("スパイク＋ブロック", "scene", "block", DemoCatalog.VOLLEY_ID),
            ControlAction("6人制の試合", "scene", "match", DemoCatalog.VOLLEY_ID),
            ControlAction("レシーブ", "scene", "receive", DemoCatalog.VOLLEY_ID),
        )),
        ControlGroup("Energy Duel", listOf(
            ControlAction("チュートリアル", "scene", "tutorial", DemoCatalog.ENERGY_DUEL_ID),
            ControlAction("試合", "scene", "match", DemoCatalog.ENERGY_DUEL_ID),
            ControlAction("フリープレイ", "scene", "free", DemoCatalog.ENERGY_DUEL_ID),
        )),
    )
}
