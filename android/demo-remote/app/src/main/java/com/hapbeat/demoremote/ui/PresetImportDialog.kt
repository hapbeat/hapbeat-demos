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
import com.hapbeat.demoremote.data.PresetTransfer.NameConflict
import com.hapbeat.demoremote.data.TransferResult

/**
 * Confirmation before presets from a QR / link are stored: name and demo order (with options) of each one, and
 * for a name already in use the choice between overwriting and adding under a new name. Importing never starts
 * the Hub or a session. A refused payload shows only the reason.
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
            // Default for a taken name: add renamed (nothing on the phone is lost unless the user picks overwrite).
            val choices = remember(pending) { pending.presets.map { NameConflict.ADD_RENAMED }.toMutableStateList() }
            AlertDialog(
                // Only the buttons close it: a tap outside (e.g. while unlocking the phone) must not drop the import.
                onDismissRequest = {},
                properties = DialogProperties(dismissOnBackPress = false, dismissOnClickOutside = false),
                title = { Text("プリセットを取り込む（${pending.presets.size} 件）") },
                text = {
                    Column(Modifier.heightIn(max = 420.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text("取り込むだけで、Hub やセッションは起動しません", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        pending.presets.forEachIndexed { i, preset ->
                            Column(Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surfaceVariant).padding(8.dp)) {
                                Text(preset.name, style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold, maxLines = 2, overflow = TextOverflow.Ellipsis)
                                preset.steps.forEachIndexed { n, step ->
                                    Text("${n + 1}. ${stepSummary(step)}", style = MaterialTheme.typography.bodySmall)
                                }
                                if (vm.presets.any { it.name == preset.name }) {
                                    Text("同じ名前のプリセットがあります", style = MaterialTheme.typography.bodySmall, color = StatusRed)
                                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                                        FilterChip(selected = choices[i] == NameConflict.OVERWRITE, onClick = { choices[i] = NameConflict.OVERWRITE }, label = { Text("上書き") })
                                        FilterChip(selected = choices[i] == NameConflict.ADD_RENAMED, onClick = { choices[i] = NameConflict.ADD_RENAMED }, label = { Text("別名で追加") })
                                    }
                                }
                            }
                        }
                    }
                },
                confirmButton = {
                    TextButton(onClick = { vm.confirmImport(choices.toList()) }, modifier = Modifier.width(96.dp)) { Text("取り込む") }
                },
                dismissButton = {
                    TextButton(onClick = vm::dismissImport) { Text("やめる") }
                },
            )
        }
    }
}
