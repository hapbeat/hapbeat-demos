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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.hapbeat.demoremote.AdbState
import com.hapbeat.demoremote.ControlCatalog
import com.hapbeat.demoremote.DemoCatalog
import com.hapbeat.demoremote.LogState
import com.hapbeat.demoremote.RemoteViewModel
import com.hapbeat.demoremote.adb.DeviceAddress
import com.hapbeat.demoremote.adb.DeviceAddressFile
import com.hapbeat.demoremote.data.HubSettingsAccess
import com.hapbeat.demoremote.data.HubSettingsSnapshot
import com.hapbeat.demoremote.protocol.DemoSwitchProtocol

/**
 * The selected HMD's Hub-wide settings (what the Hub's manage screen offers besides the presets): the in-view button
 * defaults, the hand look, the narration volume, staff waiting mode and the demo tiles of the top screen. Starts from the settings last read;
 * 保存 sends all of them with HUB_SETTINGS_SET and reads them again. The device address goes to the demos' files over adb
 * ([DeviceAddressSection]).
 */
@Composable
fun HubSettingsScreen(vm: RemoteViewModel, onDone: () -> Unit) {
    val read = vm.selectedHubSettings
    // A new read (after saving or 再読込) starts the draft again from what the Hub has.
    var hapticsUi by remember(read) { mutableStateOf(read?.hapticsUi ?: false) }
    var recenterUi by remember(read) { mutableStateOf(read?.recenterUi ?: false) }
    var handStyle by remember(read) { mutableStateOf(read?.handStyle ?: "ghost") }
    var narrationVolume by remember(read) { mutableStateOf(read?.narrationVolume ?: DemoSwitchProtocol.DEFAULT_NARRATION_VOLUME) }
    var staffWaiting by remember(read) { mutableStateOf(read?.staffWaiting ?: false) }
    var visible by remember(read) { mutableStateOf(read?.demos?.filter { it.visible }?.map { it.demoId }?.toSet() ?: emptySet()) }
    val draft = read?.copy(
        hapticsUi = hapticsUi, recenterUi = recenterUi, handStyle = handStyle, narrationVolume = narrationVolume, staffWaiting = staffWaiting,
        demos = read.demos.map { it.copy(visible = it.demoId in visible) },
    )
    // A change after a save makes its result stale.
    LaunchedEffect(draft) { if (!vm.hubSettingsSaving) vm.clearHubSettingsSaveStatus() }
    LaunchedEffect(Unit) {
        vm.clearHubHapticsTestStatus()
        // The reasons below come from the last STATE: ask for a fresh one now.
        vm.refreshHubState()
        // The device address files may have been changed from a PC since the adb connect.
        vm.readDeviceAddress()
    }
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
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("ナレーション音量", Modifier.weight(1f))
                TextButton(
                    onClick = { narrationVolume -= DemoSwitchProtocol.NARRATION_VOLUME_STEP },
                    enabled = canEdit && narrationVolume > DemoSwitchProtocol.NARRATION_VOLUMES.first,
                ) { Text("−") }
                // Fixed width so "0%" / "100%" do not move the buttons.
                Text("$narrationVolume%", Modifier.width(56.dp), textAlign = TextAlign.Center)
                TextButton(
                    onClick = { narrationVolume += DemoSwitchProtocol.NARRATION_VOLUME_STEP },
                    enabled = canEdit && narrationVolume < DemoSwitchProtocol.NARRATION_VOLUMES.last,
                ) { Text("＋") }
            }
            // Fixed line, as on the Hub: a demo older than the narration volume rejects a ticket that carries it (sent unless 50).
            Text(
                if (narrationVolume != DemoSwitchProtocol.DEFAULT_NARRATION_VOLUME) "全デモがナレーション対応版になってから変えてください" else " ",
                Modifier.fillMaxWidth().height(20.dp), style = MaterialTheme.typography.bodySmall, color = StatusRed,
                maxLines = 1, overflow = TextOverflow.Ellipsis,
            )
            SettingSwitch("スタッフ待機モード（トップ画面でスタッフを待つ）", staffWaiting, canEdit) { staffWaiting = it }
            DeviceAddressSection(vm, read)
            // Any Hub screen accepts it, the manage screen included; no confirmation (a staff operation).
            val hubInFront = vm.selectedQuest?.remoteState?.currentDemoId == DemoCatalog.HUB_ID
            ActionButton(
                ControlCatalog.hubHapticsTest.label,
                enabled = vm.demoSwitchBlockReason == null && hubInFront,
                modifier = Modifier.fillMaxWidth(),
                outlined = true,
            ) { vm.sendHubHapticsTest() }
            // Fixed line: the 触覚テスト result (READY / why it failed), or why it cannot be sent.
            val test = vm.hubHapticsTestStatus
            val (testText, testColor) = when {
                test != null -> test.first to when (test.second) {
                    LogState.READY -> StatusGreen
                    LogState.SENT, LogState.ACK -> MaterialTheme.colorScheme.onSurfaceVariant
                    else -> StatusRed
                }
                !hubInFront -> "Hub が前面のときだけ使えます" to MaterialTheme.colorScheme.onSurfaceVariant
                else -> " " to MaterialTheme.colorScheme.onSurfaceVariant
            }
            Text(
                testText, Modifier.fillMaxWidth().height(20.dp), style = MaterialTheme.typography.bodySmall, color = testColor,
                maxLines = 1, overflow = TextOverflow.Ellipsis,
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

/**
 * 「この HMD の Hapbeat 宛先」: edits player / group and writes them over adb to every known demo of the HMD
 * ([RemoteViewModel.writeDeviceAddress], no dialog). Starts from the file read over adb (the Hub's, else the first demo's).
 * The text lines have fixed heights so status changes do not move the controls.
 */
@Composable
private fun DeviceAddressSection(vm: RemoteViewModel, read: HubSettingsSnapshot?) {
    val summary = vm.selectedQuest?.takeIf { it.adb == AdbState.CONNECTED }?.address
    val current = summary?.let { DeviceAddressFile.effective(it.primary) }
    // A new read (after writing, or another HMD) starts the draft again from the file.
    var group by remember(current) { mutableStateOf(current?.group ?: DeviceAddressFile.UNSET) }
    var player by remember(current) { mutableStateOf(current?.player ?: DeviceAddressFile.UNSET) }
    val block = vm.deviceAddressBlockReason
    val canEdit = block == null && !vm.deviceAddressWriting
    Text("この HMD の Hapbeat 宛先", style = MaterialTheme.typography.titleSmall, modifier = Modifier.padding(top = 4.dp))
    // Fixed line: why it cannot be written, else what the files hold now.
    val currentText = when {
        block != null -> block
        summary == null -> "読み込み中…"
        current == null -> "現在: 読めないファイルがあります"
        else -> "現在: " + HubSettingsAccess.addressText(current.player, current.group)
    } + if (block == null && summary?.mixed == true) "（デモによって違います）" else ""
    Text(
        currentText, Modifier.fillMaxWidth().height(20.dp), style = MaterialTheme.typography.bodySmall,
        color = if (block != null) MaterialTheme.colorScheme.onSurfaceVariant else Color.Unspecified,
        maxLines = 1, overflow = TextOverflow.Ellipsis,
    )
    // Fixed line: the Hub's HUB_SETTINGS still carries what it read at its start.
    val hub = read?.let { DeviceAddress(it.player, it.group) }
    Text(
        if (block == null && hub != null && DeviceAddressFile.hubIsStale(summary, hub)) {
            "Hub は再起動前の値です（Hub: ${HubSettingsAccess.addressText(hub.player, hub.group)}）"
        } else {
            " "
        },
        Modifier.fillMaxWidth().height(20.dp), style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis,
    )
    AddressStepper("グループ", group, canEdit) { group = it }
    // The groups used most, chosen directly.
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        listOf(DeviceAddressFile.UNSET, 1, 2, 3).forEach { value ->
            FilterChip(
                selected = group == value, enabled = canEdit, onClick = { group = value },
                label = { Text(DeviceAddressFile.axisText(value)) },
            )
        }
    }
    AddressStepper("プレイヤー", player, canEdit) { player = it }
    // Fixed line: the write result ("書けた n 本 / 失敗 m 本（…）").
    val status = vm.deviceAddressStatus
    Text(
        status?.first ?: " ", Modifier.fillMaxWidth().height(20.dp), style = MaterialTheme.typography.bodySmall,
        color = when (status?.second) {
            LogState.READY -> StatusGreen
            LogState.SENT, null -> MaterialTheme.colorScheme.onSurfaceVariant
            else -> StatusRed
        },
        maxLines = 1, overflow = TextOverflow.Ellipsis,
    )
    ActionButton(
        if (vm.deviceAddressWriting) "書き込み中…" else "書き込む",
        enabled = canEdit,
        modifier = Modifier.fillMaxWidth(),
    ) { vm.writeDeviceAddress(DeviceAddress(player, group)) }
    // Fixed area after a write: running demos keep the old value until restarted; the same restart as the main screen's.
    val written = status != null && status.second != LogState.SENT && !vm.deviceAddressWriting
    Row(Modifier.fillMaxWidth().height(48.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(
            if (written) "起動中のデモは再起動で反映されます" else " ",
            Modifier.weight(1f), style = MaterialTheme.typography.bodySmall, maxLines = 2, overflow = TextOverflow.Ellipsis,
        )
        if (written) {
            TextButton(
                onClick = vm::restartFrontAppByAdb,
                enabled = !vm.portActionRunning && vm.selectedQuest?.adb == AdbState.CONNECTED,
            ) { Text("前面のアプリを再起動") }
        }
    }
    // Fixed line: the restart's progress / result (shared with the main screen's diagnosis area).
    val (portText, portError) = vm.portStatus
    Text(
        if (written) portText.ifEmpty { " " } else " ", Modifier.fillMaxWidth().height(20.dp), style = MaterialTheme.typography.bodySmall,
        color = if (portError) StatusRed else MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis,
    )
}

/** "グループ  −  指定なし  ＋": 指定なし and 1..99 ([DeviceAddressFile.step]). */
@Composable
private fun AddressStepper(label: String, value: Int, enabled: Boolean, onChange: (Int) -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(label, Modifier.weight(1f))
        TextButton(onClick = { onChange(DeviceAddressFile.step(value, -1)) }, enabled = enabled && value != DeviceAddressFile.UNSET) { Text("−") }
        // Fixed width so "指定なし" / "99" do not move the buttons.
        Text(DeviceAddressFile.axisText(value), Modifier.width(80.dp), textAlign = TextAlign.Center)
        TextButton(
            onClick = { onChange(DeviceAddressFile.step(value, 1)) },
            enabled = enabled && value != DeviceAddressFile.VALUES.last,
        ) { Text("＋") }
    }
}

@Composable
private fun SettingSwitch(label: String, checked: Boolean, enabled: Boolean, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Text(label, Modifier.weight(1f))
        Switch(checked = checked, onCheckedChange = onChange, enabled = enabled)
    }
}
