package com.hapbeat.demoremote.data

import com.hapbeat.demoremote.DemoCatalog
import com.hapbeat.demoremote.protocol.DemoSwitchMessage
import com.hapbeat.demoremote.protocol.DemoSwitchProtocol

/**
 * One Hub slot as last read with PRESET_GET. [stepCount] is the Hub's count; [unreadable]: the step after
 * [preset].steps alone does not fit in one PRESET datagram, so the slot can only be edited on the Hub.
 */
data class HubPresetSlot(val number: Int, val revision: Long, val preset: HubPreset, val stepCount: Int, val unreadable: Boolean)

/** Outcome of reading one Hub preset. */
sealed interface PresetRead {
    data class Read(val slot: HubPresetSlot) : PresetRead
    /** A page got no PRESET (the Hub is not running, or the reply was lost). */
    data object NoResponse : PresetRead
    /** The pages do not fit together (wrong preset / from, more steps than step_count) or the revision kept changing. */
    data object Inconsistent : PresetRead
}

/**
 * Reading, checking and writing the Hub's presets 1..3 over Demo Switch (demo-switch-control.md「Hub presets」).
 * Pure Kotlin so the paging and validation rules run in JVM unit tests.
 */
object HubPresets {
    val NUMBERS: List<Int> = DemoSwitchProtocol.PRESET_NUMBERS.toList()

    /** Restarts from 0 after a revision change at most this often before giving up. */
    const val MAX_REVISION_RESTARTS = 3

    /**
     * Reads preset [number] page by page: asks [fetch] from `from` = steps read so far until `step_count` steps are
     * in. A revision change between pages starts again from 0. An empty page before `step_count` means that step is
     * too large to read ([HubPresetSlot.unreadable]).
     */
    suspend fun read(number: Int, fetch: suspend (from: Int) -> DemoSwitchMessage.Preset?): PresetRead {
        var restarts = 0
        var revision: Long? = null
        val steps = mutableListOf<PresetStep>()
        while (true) {
            val from = steps.size
            val page = fetch(from) ?: return PresetRead.NoResponse
            if (page.preset != number || page.from != from) return PresetRead.Inconsistent
            if (revision != null && page.revision != revision) {
                if (++restarts > MAX_REVISION_RESTARTS) return PresetRead.Inconsistent
                revision = null
                steps.clear()
                continue
            }
            revision = page.revision
            val unreadable = from < page.stepCount && page.steps.isEmpty()
            steps += page.steps
            if (steps.size > page.stepCount) return PresetRead.Inconsistent
            if (unreadable || steps.size == page.stepCount) {
                return PresetRead.Read(HubPresetSlot(number, page.revision, HubPreset(page.name, page.visible, steps.toList()), page.stepCount, unreadable))
            }
        }
    }

    /** The steps sent to the Hub: values of options that do not apply (descriptor `when`) are left out. */
    fun normalize(steps: List<PresetStep>): List<PresetStep> =
        steps.map { it.copy(options = DemoCatalog.applicableOptions(it.demoId, it.options)) }

    /** 1024 bytes minus the worst-case PRESET_SET (largest sequence, with auth) for [preset] in slot [number]. */
    fun remainingBytes(controllerId: String, number: Int, preset: HubPreset): Int = DemoSwitchProtocol.MAX_PAYLOAD_BYTES -
        DemoSwitchProtocol.presetSetWorstCaseBytes(controllerId, number, preset.name, preset.visible, normalize(preset.steps))

    /**
     * Why [preset] cannot be written to slot [number], or null: the name rules, 0..32 steps, then [stepsProblem], then
     * the 1024-byte limit.
     */
    fun problem(controllerId: String, number: Int, preset: HubPreset, hubDemos: List<HubDemo>? = null): String? {
        if (number !in DemoSwitchProtocol.PRESET_NUMBERS) return "プリセットの番号が正しくありません"
        if (!DemoSwitchProtocol.isPresetName(preset.name)) return "名前が正しくありません（40 文字まで・改行なし）"
        if (preset.steps.size > DemoSwitchProtocol.MAX_PRESET_STEPS) return "デモは ${DemoSwitchProtocol.MAX_PRESET_STEPS} 本までです"
        stepsProblem(preset.steps, hubDemos)?.let { return it }
        if (remainingBytes(controllerId, number, preset) < 0) return "大きすぎて送れません（デモ数か名前を減らしてください）"
        return null
    }

    /**
     * Why [steps] would be refused by the Hub (PRESET_SET / HUB_START), or null. With the Hub's installed demos
     * ([hubDemos], from HUB_SETTINGS) every demo must be installed there; a demo this app knows must use its option
     * table, and one it does not know is sent without options (the demo's defaults). Without that list only this app's
     * session demos are allowed.
     */
    fun stepsProblem(steps: List<PresetStep>, hubDemos: List<HubDemo>?): String? {
        for (step in steps) {
            val label = DemoCatalog.labelFor(step.demoId, hubDemos)
            val known = DemoCatalog.sessionApps.any { it.demoId == step.demoId }
            if (hubDemos != null) {
                if (hubDemos.none { it.demoId == step.demoId }) return "$label が HMD の Hub に入っていません"
            } else if (!known) {
                return "リモコンに登録されていないデモです: ${step.demoId}"
            }
            if (!known) {
                if (step.options.isNotEmpty()) return "$label にない設定があります"
                continue
            }
            try {
                DemoCatalog.requireValidStep(step.demoId, step.options)
            } catch (_: IllegalArgumentException) {
                return "$label にない設定があります"
            }
        }
        return null
    }

    /**
     * FAILED of PRESET_SET / PRESET_START / HUB_SETTINGS_SET / HUB_START in words; [message] names the first offending
     * demo_id where the Hub gives one (a demo of this app or of [hubDemos]).
     */
    fun failureText(code: String, message: String, hubDemos: List<HubDemo>? = null): String {
        val demo = message.takeIf { id ->
            DemoCatalog.sessionApps.any { it.demoId == id } || hubDemos?.any { it.demoId == id } == true
        }
        return when {
            message == DemoSwitchProtocol.NOT_IN_FOREGROUND_MESSAGE -> HUB_NOT_FOREGROUND
            code == "invalid_payload" && demo != null -> "${DemoCatalog.labelFor(demo, hubDemos)} にない設定です（Hub とリモコンの版を確認）"
            code == "not_allowed" && demo != null -> "${DemoCatalog.labelFor(demo, hubDemos)} が HMD にインストールされていません"
            code == "not_allowed" -> "Hub が受け付けませんでした（管理画面の表示中・起動中・デモが無い枠など）" +
                if (message.isEmpty()) "" else ": $message"
            code == "launch_failed" -> "Hub で失敗しました" + if (message.isEmpty()) "" else ": $message"
            message.isEmpty() -> code
            else -> "$code: $message"
        }
    }

    const val HUB_NOT_RUNNING = "Hub が起動していません（「Hub を開く」で開けます）"
    const val HUB_NOT_FOREGROUND = "Hub が前面にありません（HMD 内でメニューや一時停止を閉じてください）"
    /** STATE `screen` manage: the Hub refuses preset and settings writes until the manage screen is closed. */
    const val HUB_MANAGE_OPEN = "Hub の管理画面が開いています（閉じてから操作してください）"
}
