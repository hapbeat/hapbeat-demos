package com.hapbeat.demoremote.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
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
import com.hapbeat.demoremote.RemoteViewModel
import com.hapbeat.demoremote.data.PresetStep
import com.hapbeat.demoremote.data.RemotePreset

/** Builds a remote preset: name, demos in order, tutorial per demo. [index] null = new preset. */
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
                    onTutorial = { steps[i] = step.copy(tutorial = it) },
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
    i: Int, step: PresetStep, onTutorial: (String?) -> Unit, onUp: () -> Unit, onDown: () -> Unit, onRemove: () -> Unit,
) {
    Column(Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surfaceVariant).padding(horizontal = 8.dp, vertical = 4.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("${i + 1}. ${DemoCatalog.labelFor(step.demoId)}", Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis)
            TextButton(onClick = onUp, modifier = Modifier.width(48.dp)) { Text("↑") }
            TextButton(onClick = onDown, modifier = Modifier.width(48.dp)) { Text("↓") }
            TextButton(onClick = onRemove, modifier = Modifier.width(56.dp)) { Text("削除") }
        }
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            Text("チュートリアル", style = MaterialTheme.typography.bodySmall, modifier = Modifier.weight(1f))
            TUTORIAL_CHOICES.forEach { (value, label) ->
                FilterChip(selected = step.tutorial == value, onClick = { onTutorial(value) }, label = { Text(label) })
            }
        }
    }
}

/** Tutorial option choices shared by the single-demo start and the preset editor. */
val TUTORIAL_CHOICES: List<Pair<String?, String>> = listOf(null to "既定", "on" to "あり", "off" to "なし")

/** Hub ticket limit (demo-session.md: steps 1..32). */
const val MAX_STEPS = 32

/** "Hand Demo（T）→ T-Rex Encounter" style one-line summary of a preset. */
fun presetSummary(preset: RemotePreset): String = preset.steps.joinToString(" → ") { step ->
    DemoCatalog.labelFor(step.demoId) + when (step.tutorial) { "on" -> "（T あり）"; "off" -> "（T なし）"; else -> "" }
}
