package com.hapbeat.demoremote

/**
 * Fixed demo table. adb launches only these packages; nothing received from the
 * network is ever used as a package name or shell command.
 */
data class DemoApp(val demoId: String, val label: String, val packageName: String)

object DemoCatalog {
    const val HUB_ID = "demo_hub"
    const val VOLLEY_ID = "volley"

    val apps: List<DemoApp> = listOf(
        DemoApp(HUB_ID, "Demo Hub", "jp.hapbeat.demohub"),
        DemoApp(VOLLEY_ID, "Volley", "jp.hapbeat.volley"),
        DemoApp("boxing", "Boxing", "com.hapbeat.boxing"),
        DemoApp("handdemo", "Hand Demo", "com.Hapbeat.HapticHandDemo_G2"),
        DemoApp("trex-encounter", "T-Rex Encounter", "com.hapbeat.trexencounter"),
        DemoApp("safety-mill", "Safety Mill", "com.hapbeat.safetymill"),
        DemoApp("energy-duel", "Energy Duel", "jp.hapbeat.energyduel"),
        DemoApp("fps", "FPS", "com.hapbeat.fpsdemo"),
    )

    val hub: DemoApp get() = apps.first { it.demoId == HUB_ID }

    /** Display name for a demo ID; unknown IDs are shown as-is. */
    fun labelFor(demoId: String?): String = apps.firstOrNull { it.demoId == demoId }?.label ?: (demoId ?: "-")

    /** Demo IDs that can start a one-demo Hub session (everything but the Hub itself). */
    val sessionApps: List<DemoApp> get() = apps.filter { it.demoId != HUB_ID }

    private const val HUB_COMPONENT = "jp.hapbeat.demohub/com.unity3d.player.UnityPlayerGameActivity"
    private const val HUB_START_EXTRA = "com.hapbeat.demo_hub.start"

    /**
     * Starts a Demo Session through the Hub's external start extra (demo-session.md「Hub を外部から起動して
     * セッションを始める」): a preset 1..3, or one demo with an optional tutorial option ("on" / "off").
     * The JSON is built only from the fixed table and literals, so it never contains a single quote.
     */
    fun hubSessionCommand(preset: Int? = null, demoId: String? = null, tutorial: String? = null): String {
        val json = when {
            preset != null -> {
                require(preset in 1..3)
                "{\"version\":1,\"preset\":$preset}"
            }
            demoId != null -> {
                require(sessionApps.any { it.demoId == demoId })
                require(tutorial == null || tutorial == "on" || tutorial == "off")
                val options = if (tutorial == null) "" else ",\"options\":{\"tutorial\":\"$tutorial\"}"
                "{\"version\":1,\"demo_id\":\"$demoId\"$options}"
            }
            else -> throw IllegalArgumentException("preset or demoId")
        }
        return "am start -n $HUB_COMPONENT --es $HUB_START_EXTRA '$json'"
    }

    /** Shell command that resolves the launcher activity of [app] and starts it. */
    fun launchCommand(app: DemoApp): String =
        "c=\$(cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER " +
            "${app.packageName} | tail -n 1) && am start -n \"\$c\""
}

/** One CONTROL button: action + scene_id (empty except for scene). */
data class ControlAction(val label: String, val action: String, val sceneId: String = "")

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
            ControlAction("スパイク＋ブロック", "scene", "block"),
            ControlAction("6人制の試合", "scene", "match"),
            ControlAction("レシーブ", "scene", "receive"),
        )),
    )
}
