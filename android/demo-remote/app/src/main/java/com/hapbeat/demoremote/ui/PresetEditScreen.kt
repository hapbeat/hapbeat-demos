package com.hapbeat.demoremote.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
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
import com.hapbeat.demoremote.RemoteViewModel
import com.hapbeat.demoremote.data.PresetStep
import com.hapbeat.demoremote.data.RemotePreset

/** Builds a remote preset: name, demos in order, options and retry per demo. [index] null = new preset. */
@Composable
fun PresetEditScreen(vm: RemoteViewModel, index: Int?, onDone: () -> Unit) {
    val original = index?.let { vm.presets.getOrNull(it) }
    var name by remember { mutableStateOf(original?.name ?: "プリセット ${vm.presets.size + 1}") }
    val steps = remember { (original?.steps ?: emptyList()).toMutableStateList() }
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(
            Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                TextButton(onClick = onDone) { Text("← キャンセル") }
                Text(if (original == null) "プリセットを作る" else "プリセットを編集", style = MaterialTheme.typography.titleLarge)
            }
            OutlinedTextField(
                value = name, onValueChange = { name = it.take(40) }, label = { Text("名前") }, singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
            Text("順番（上から実行）", style = MaterialTheme.typography.titleSmall)
            if (steps.isEmpty()) Text("下のボタンでデモを追加してください", style = MaterialTheme.typography.bodySmall)
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
            ButtonGrid(DemoCatalog.sessionApps, columns = 2) { app, modifier ->
                ActionButton(app.label, enabled = steps.size < MAX_STEPS, modifier = modifier, outlined = true) {
                    steps.add(PresetStep(app.demoId))
                }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                ActionButton("保存", enabled = name.isNotBlank() && steps.isNotEmpty(), modifier = Modifier.weight(1f)) {
                    vm.savePreset(index, RemotePreset(name.trim(), steps.toList()))
                    onDone()
                }
                if (index != null) {
                    ActionButton("削除", enabled = true, modifier = Modifier.weight(1f), outlined = true) {
                        vm.deletePreset(index)
                        onDone()
                    }
                }
            }
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
        OptionChooser(DemoCatalog.optionsFor(step.demoId), step.options) { onChange(step.copy(options = it)) }
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

/** Hub ticket limit (demo-session.md: steps 1..32). */
const val MAX_STEPS = 32

/** "Energy Duel（チュートリアル: なし）" plus "・やり直しなし" when retry is off. */
fun stepSummary(step: PresetStep): String {
    val options = DemoCatalog.optionSummary(step.demoId, step.options)
    return DemoCatalog.labelFor(step.demoId) + (if (options.isEmpty()) "" else "（$options）") + if (step.retry) "" else "・やり直しなし"
}

/** "Hand Demo → Energy Duel（モード: 試合）" style one-line summary of a preset. */
fun presetSummary(preset: RemotePreset): String = preset.steps.joinToString(" → ") { stepSummary(it) }
