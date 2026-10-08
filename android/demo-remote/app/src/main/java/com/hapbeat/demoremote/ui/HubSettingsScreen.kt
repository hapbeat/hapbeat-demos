package com.hapbeat.demoremote.ui

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
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.hapbeat.demoremote.DemoCatalog
import com.hapbeat.demoremote.LogState
import com.hapbeat.demoremote.RemoteViewModel
import com.hapbeat.demoremote.data.HubSettingsAccess

/**
 * The selected HMD's Hub-wide settings (what the Hub's manage screen offers besides the presets): the in-view button
 * defaults, the hand look, staff waiting mode and the demo tiles of the top screen. Starts from the settings last read;
 * 保存 sends all of them with HUB_SETTINGS_SET and reads them again. The device address is shown read only.
 */
@Composable
fun HubSettingsScreen(vm: RemoteViewModel, onDone: () -> Unit) {
    val read = vm.selectedHubSettings
    // A new read (after saving or 再読込) starts the draft again from what the Hub has.
    var hapticsUi by remember(read) { mutableStateOf(read?.hapticsUi ?: false) }
    var recenterUi by remember(read) { mutableStateOf(read?.recenterUi ?: false) }
    var handStyle by remember(read) { mutableStateOf(read?.handStyle ?: "ghost") }
    var staffWaiting by remember(read) { mutableStateOf(read?.staffWaiting ?: false) }
    var visible by remember(read) { mutableStateOf(read?.demos?.filter { it.visible }?.map { it.demoId }?.toSet() ?: emptySet()) }
    val draft = read?.copy(
        hapticsUi = hapticsUi, recenterUi = recenterUi, handStyle = handStyle, staffWaiting = staffWaiting,
        demos = read.demos.map { it.copy(visible = it.demoId in visible) },
    )
    // A change after a save makes its result stale.
    LaunchedEffect(draft) { if (!vm.hubSettingsSaving) vm.clearHubSettingsSaveStatus() }
    val block = vm.hubPresetBlockReason
    val problem = draft?.let { HubSettingsAccess.problem(vm.controllerId, it) }
    val canEdit = read != null
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(
            Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                TextButton(onClick = onDone) { Text("← 戻る") }
                Text("Hub の設定", style = MaterialTheme.typography.titleLarge, modifier = Modifier.weight(1f), maxLines = 1)
                // Fixed width so the label swap does not shift the heading.
                TextButton(
                    onClick = vm::readHubSettings,
                    enabled = !vm.hubSettingsReading && vm.selectedQuest?.remoteState?.currentDemoId == DemoCatalog.HUB_ID,
                    modifier = Modifier.width(96.dp),
                ) { Text(if (vm.hubSettingsReading) "読込中" else "再読込") }
            }
            Text(
                "${vm.selectedQuest?.label ?: "-"} の Hub の設定です（HMD ごとに別です）。Hub の管理画面と同じ内容で、すべての起動に効きます",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            // Two fixed lines: why saving is off (else when the settings were read), then whether this is the last read.
            Column(Modifier.fillMaxWidth()) {
                Text(
                    block ?: vm.hubSettingsStatus.ifEmpty { if (read == null) "Hub を開くと読み込みます" else " " },
                    style = MaterialTheme.typography.bodySmall,
                    color = if (block != null) StatusRed else MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    if (block != null && read != null) "下は最後に読んだ内容です" else " ",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                )
            }
            HubManageNotice(vm)
            SettingSwitch("触覚ボタンを表示（起動時の既定）", hapticsUi, canEdit) { hapticsUi = it }
            SettingSwitch("視線リセットボタンを表示（起動時の既定）", recenterUi, canEdit) { recenterUi = it }
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("手の見た目", Modifier.weight(1f))
                FilterChip(selected = handStyle == "ghost", enabled = canEdit, onClick = { handStyle = "ghost" }, label = { Text("ゴースト") })
                FilterChip(selected = handStyle == "skin", enabled = canEdit, onClick = { handStyle = "skin" }, label = { Text("肌") })
            }
            SettingSwitch("スタッフ待機モード（トップ画面でスタッフを待つ）", staffWaiting, canEdit) { staffWaiting = it }
            Text(
                "この HMD の宛先: " + (read?.let { HubSettingsAccess.addressText(it.player, it.group) } ?: "-") + "（adb で書き込みます）",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text("トップに出すデモ（押すと表示 / 非表示）", style = MaterialTheme.typography.titleSmall)
            val demos = read?.demos.orEmpty()
            if (read != null && demos.isEmpty()) Text("Hub にデモが入っていません", style = MaterialTheme.typography.bodySmall)
            // Filled = shown on the top screen, outlined = hidden.
            ButtonGrid(DemoCatalog.sessionCandidates(demos), columns = 3) { app, modifier ->
                val shown = app.demoId in visible
                DemoTile(app, enabled = canEdit, modifier = modifier, outlined = !shown) {
                    visible = if (shown) visible - app.demoId else visible + app.demoId
                }
            }
            // Fixed line: the save result / why saving is not possible.
            val save = vm.hubSettingsSaveStatus
            val (status, statusColor) = when {
                save != null -> save.first to when (save.second) {
                    LogState.READY -> StatusGreen
                    LogState.SENT, LogState.ACK -> MaterialTheme.colorScheme.onSurfaceVariant
                    else -> StatusRed
                }
                problem != null -> problem to StatusRed
                else -> " " to MaterialTheme.colorScheme.onSurfaceVariant
            }
            Text(
                status, Modifier.fillMaxWidth().height(20.dp), style = MaterialTheme.typography.bodySmall, color = statusColor,
                maxLines = 1, overflow = TextOverflow.Ellipsis,
            )
            ActionButton(
                if (vm.hubSettingsSaving) "保存中…" else "Hub に保存",
                enabled = draft != null && problem == null && block == null && !vm.hubSettingsSaving,
                modifier = Modifier.fillMaxWidth(),
            ) { draft?.let(vm::saveHubSettings) }
        }
    }
}

@Composable
private fun SettingSwitch(label: String, checked: Boolean, enabled: Boolean, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Text(label, Modifier.weight(1f))
        Switch(checked = checked, onCheckedChange = onChange, enabled = enabled)
    }
}
