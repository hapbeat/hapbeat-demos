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
        DemoApp("gloveball_v2", "GloveBall", "jp.hapbeat.gloveballdemo.v2"),
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

    /** Shell command that resolves the launcher activity of [app] and starts it. */
    fun launchCommand(app: DemoApp): String =
        "c=\$(cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER " +
            "${app.packageName} | tail -n 1) && am start -n \"\$c\""
}

/** One CONTROL button: action + scene_id (empty except for scene). */
data class ControlAction(val label: String, val action: String, val sceneId: String = "")

object ControlCatalog {
    val actions: List<ControlAction> = listOf(
        ControlAction("メニューを開く", "menu_open"),
        ControlAction("メニューを閉じる", "menu_close"),
        ControlAction("視線リセット", "recenter"),
        ControlAction("最初から", "restart"),
        ControlAction("触覚 ON", "haptics_on"),
        ControlAction("触覚 OFF", "haptics_off"),
        ControlAction("触覚ボタン表示", "haptics_ui_show"),
        ControlAction("触覚ボタン非表示", "haptics_ui_hide"),
        ControlAction("リセットボタン表示", "recenter_ui_show"),
        ControlAction("リセットボタン非表示", "recenter_ui_hide"),
        ControlAction("Volley レシーブ", "scene", "receive"),
        ControlAction("Volley ブロック", "scene", "block"),
    )
}
