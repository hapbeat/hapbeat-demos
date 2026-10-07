package com.hapbeat.demoremote.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.runtime.toMutableStateList
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.DialogProperties
import com.hapbeat.demoremote.RemoteViewModel
import com.hapbeat.demoremote.data.HubPresetSlot
import com.hapbeat.demoremote.data.HubPresets
import com.hapbeat.demoremote.data.TransferResult

/**
 * Confirmation before presets from a QR / link are written to the selected HMD's Hub: name and demo order (with
 * options) of each one, and the slot (1..3) it goes into, saying when that slot is overwritten. Writing never starts
 * a session. While the Hub is not in front the dialog stays open and waits. A refused payload shows only the reason.
 */
@Composable
fun PresetImportDialog(vm: RemoteViewModel) {
    when (val pending = vm.pendingImport) {
        null -> Unit
        is TransferResult.Rejected -> AlertDialog(
            onDismissRequest = vm::dismissImport,
            title = { Text("取り込めません") },
            text = { Text(pending.message, color = StatusRed) },
            confirmButton = { TextButton(onClick = vm::dismissImport) { Text("閉じる") } },
        )
        is TransferResult.Accepted -> {
            // Preset i goes to slot i+1 unless the user picks another.
            val targets = remember(pending) { pending.presets.indices.map { it + 1 }.toMutableStateList() }
            val slots = vm.selectedHubPresets
            val block = vm.hubPresetBlockReason
            val duplicate = targets.toSet().size != targets.size
            AlertDialog(
                // Only the buttons close it: a tap outside (e.g. while unlocking the phone) must not drop the import.
                onDismissRequest = {},
                properties = DialogProperties(dismissOnBackPress = false, dismissOnClickOutside = false),
                title = { Text("Hub に取り込む（${pending.presets.size} 件）") },
                text = {
                    Column(Modifier.heightIn(max = 420.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(
                            "${vm.selectedQuest?.label ?: "-"} の Hub のプリセットに書き込みます。書き込むだけで、セッションは始まりません",
                            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                        pending.presets.forEachIndexed { i, preset ->
                            Column(Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surfaceVariant).padding(8.dp)) {
                                Text(preset.name, style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold, maxLines = 2, overflow = TextOverflow.Ellipsis)
                                preset.steps.forEachIndexed { n, step ->
                                    Text("${n + 1}. ${stepSummary(step)}", style = MaterialTheme.typography.bodySmall)
                                }
                                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                                    Text("書き込む枠", style = MaterialTheme.typography.bodySmall)
                                    HubPresets.NUMBERS.forEach { number ->
                                        FilterChip(selected = targets[i] == number, onClick = { targets[i] = number }, label = { Text("$number") })
                                    }
                                }
                                // Fixed line: what is in the chosen slot now.
                                val (text, overwrite) = slotNote(targets[i], slots.getOrNull(targets[i] - 1))
                                Text(text, style = MaterialTheme.typography.bodySmall, color = if (overwrite) StatusRed else MaterialTheme.colorScheme.onSurfaceVariant,
                                    maxLines = 1, overflow = TextOverflow.Ellipsis)
                            }
                        }
                        // Fixed line: progress / failure, else why writing waits.
                        val (status, error) = when {
                            vm.importStatus.first.isNotEmpty() -> vm.importStatus
                            duplicate -> "同じ枠を 2 回選んでいます" to true
                            block != null -> "Hub を開いてから取り込んでください（$block）" to true
                            else -> " " to false
                        }
                        Text(status, style = MaterialTheme.typography.bodySmall, color = if (error) StatusRed else MaterialTheme.colorScheme.onSurfaceVariant,
                            maxLines = 2, minLines = 2, overflow = TextOverflow.Ellipsis)
                    }
                },
                confirmButton = {
                    TextButton(
                        onClick = { vm.confirmImport(targets.toList()) },
                        enabled = !duplicate && block == null && !vm.importRunning,
                        modifier = Modifier.width(112.dp),
                    ) { Text("書き込む") }
                },
                dismissButton = {
                    TextButton(onClick = vm::dismissImport, enabled = !vm.importRunning) { Text("やめる") }
                },
            )
        }
    }
}

/** What writing into slot [number] replaces: (text, true when existing content is overwritten). */
private fun slotNote(number: Int, slot: HubPresetSlot?): Pair<String, Boolean> = when {
    slot == null -> "プリセット $number の中身は未確認です（あれば上書き）" to true
    slot.stepCount == 0 && slot.preset.name.isEmpty() -> "プリセット $number は空きです" to false
    else -> "上書き: プリセット $number「${slot.preset.name.ifEmpty { "名前なし" }}」（${slot.stepCount} 本）" to true
}
