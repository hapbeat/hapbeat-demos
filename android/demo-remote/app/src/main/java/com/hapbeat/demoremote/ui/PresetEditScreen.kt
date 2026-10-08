package com.hapbeat.demoremote.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Checkbox
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.toMutableStateList
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.hapbeat.demoremote.DemoCatalog
import com.hapbeat.demoremote.DemoOption
import com.hapbeat.demoremote.LogState
import com.hapbeat.demoremote.RemoteViewModel
import com.hapbeat.demoremote.data.HubPreset
import com.hapbeat.demoremote.data.HubPresets
import com.hapbeat.demoremote.data.PresetStep
import com.hapbeat.demoremote.protocol.DemoSwitchProtocol

/**
 * Edits the selected HMD's Hub preset [number] (1..3): name, whether the Hub shows it, demos in order with options
 * and retry. Starts from the content last read; 保存 writes it with PRESET_SET and shows the Hub's answer.
 */
@Composable
fun PresetEditScreen(vm: RemoteViewModel, number: Int, onDone: () -> Unit) {
    val original = vm.selectedHubPresets.getOrNull(number - 1)?.preset
    var name by remember { mutableStateOf(original?.name ?: "") }
    var visible by remember { mutableStateOf(original?.visible ?: true) }
    val steps = remember { (original?.steps ?: emptyList()).toMutableStateList() }
    val draft = HubPreset(name.trim(), visible, steps.toList())
    // A change after a save makes its result stale.
    LaunchedEffect(draft) { if (!vm.presetSaving) vm.clearPresetSaveStatus() }
    val remaining = HubPresets.remainingBytes(vm.controllerId, number, draft)
    val problem = HubPresets.problem(vm.controllerId, number, draft)
    val block = vm.hubPresetBlockReason
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(
            Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                TextButton(onClick = onDone) { Text("← 戻る") }
                Text("プリセット $number を編集", style = MaterialTheme.typography.titleLarge, maxLines = 1)
            }
            Text(
                "${vm.selectedQuest?.label ?: "-"} の Hub に保存します（HMD ごとに別です）",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            OutlinedTextField(
                value = name, onValueChange = { name = takeCodePoints(it, NAME_MAX_CODE_POINTS) }, label = { Text("名前（空でも可）") },
                singleLine = true, modifier = Modifier.fillMaxWidth(),
            )
            Row(Modifier.fillMaxWidth().clickable { visible = !visible }, verticalAlignment = Alignment.CenterVertically) {
                Checkbox(checked = visible, onCheckedChange = { visible = it })
                Text("Hub のトップ画面に表示する", style = MaterialTheme.typography.bodyMedium)
            }
            Text("順番（上から実行）", style = MaterialTheme.typography.titleSmall)
            if (steps.isEmpty()) Text("デモが無いまま保存すると空きになります", style = MaterialTheme.typography.bodySmall)
            steps.forEachIndexed { i, step ->
                StepRow(
                    i, step,
                    onChange = { steps[i] = it },
                    onUp = { if (i > 0) { steps.removeAt(i); steps.add(i - 1, step) } },
                    onDown = { if (i < steps.lastIndex) { steps.removeAt(i); steps.add(i + 1, step) } },
                    onRemove = { steps.removeAt(i) },
                )
            }
            Text("デモを追加", style = MaterialTheme.typography.titleSmall)
            ButtonGrid(DemoCatalog.sessionApps, columns = 3) { app, modifier ->
                DemoTile(app, enabled = steps.size < DemoSwitchProtocol.MAX_PRESET_STEPS, modifier = modifier, outlined = true) {
                    steps.add(PresetStep(app.demoId))
                }
            }
            // Fixed two lines: remaining bytes, then the save result / why saving is not possible.
            val save = vm.presetSaveStatus
            val (status, statusColor) = when {
                save != null -> save.first to when (save.second) {
                    LogState.READY -> StatusGreen
                    LogState.SENT, LogState.ACK -> MaterialTheme.colorScheme.onSurfaceVariant
                    else -> StatusRed
                }
                problem != null -> problem to StatusRed
                block != null -> block to StatusRed
                else -> " " to MaterialTheme.colorScheme.onSurfaceVariant
            }
            Column(Modifier.fillMaxWidth().height(40.dp)) {
                Text(
                    "残り $remaining バイト（送信 1 回 ${DemoSwitchProtocol.MAX_PAYLOAD_BYTES} バイトまで）",
                    style = MaterialTheme.typography.bodySmall,
                    color = if (remaining < 0) StatusRed else MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                )
                Text(status, style = MaterialTheme.typography.bodySmall, color = statusColor, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            ActionButton(
                if (vm.presetSaving) "保存中…" else "Hub に保存",
                enabled = problem == null && block == null && !vm.presetSaving,
                modifier = Modifier.fillMaxWidth(),
            ) { vm.saveHubPreset(number, draft) }
        }
    }
}

@Composable
private fun StepRow(
    i: Int, step: PresetStep, onChange: (PresetStep) -> Unit, onUp: () -> Unit, onDown: () -> Unit, onRemove: () -> Unit,
) {
    Column(Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surfaceVariant).padding(horizontal = 8.dp, vertical = 4.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("${i + 1}. ${DemoCatalog.labelFor(step.demoId)}", Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis)
            TextButton(onClick = onUp, modifier = Modifier.width(48.dp)) { Text("↑") }
            TextButton(onClick = onDown, modifier = Modifier.width(48.dp)) { Text("↓") }
            TextButton(onClick = onRemove, modifier = Modifier.width(56.dp)) { Text("削除") }
        }
        OptionChooser(DemoCatalog.activeOptionsFor(step.demoId, step.options), step.options) {
            // Drop values of options that no longer apply (e.g. Volley balls after switching away from receive).
            onChange(step.copy(options = DemoCatalog.applicableOptions(step.demoId, it)))
        }
        Row(Modifier.fillMaxWidth().clickable { onChange(step.copy(retry = !step.retry)) }, verticalAlignment = Alignment.CenterVertically) {
            Checkbox(checked = step.retry, onCheckedChange = { onChange(step.copy(retry = it)) })
            Text("失敗時にやり直す", style = MaterialTheme.typography.bodySmall)
        }
    }
}

/**
 * One row per descriptor option of a demo: "既定" (key left out, the demo decides) or one of its values.
 * Shared by the preset editor and the single-demo start. Demos without options show nothing.
 */
@Composable
fun OptionChooser(options: List<DemoOption>, values: Map<String, String>, onChange: (Map<String, String>) -> Unit) {
    options.forEach { option ->
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(option.label, style = MaterialTheme.typography.bodySmall, modifier = Modifier.width(88.dp), maxLines = 1, overflow = TextOverflow.Ellipsis)
            Row(Modifier.weight(1f).horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                FilterChip(selected = option.id !in values, onClick = { onChange(values - option.id) }, label = { Text("既定") })
                option.values.forEach { v ->
                    FilterChip(selected = values[option.id] == v.value, onClick = { onChange(values + (option.id to v.value)) }, label = { Text(v.label) })
                }
            }
        }
    }
}

/** Hub preset name limit (demo-session.md name rules). */
private const val NAME_MAX_CODE_POINTS = 40

/** [text] cut to [max] code points (a surrogate pair is never split). */
private fun takeCodePoints(text: String, max: Int): String =
    if (text.codePointCount(0, text.length) <= max) text else text.substring(0, text.offsetByCodePoints(0, max))

/** "Energy Duel（チュートリアル: なし）" plus "・やり直しなし" when retry is off. */
fun stepSummary(step: PresetStep): String {
    val options = DemoCatalog.optionSummary(step.demoId, step.options)
    return DemoCatalog.labelFor(step.demoId) + (if (options.isEmpty()) "" else "（$options）") + if (step.retry) "" else "・やり直しなし"
}

/** "Hand Demo → Energy Duel（モード: 試合）" style one-line summary of a demo order. */
fun stepsSummary(steps: List<PresetStep>): String = steps.joinToString(" → ") { stepSummary(it) }
