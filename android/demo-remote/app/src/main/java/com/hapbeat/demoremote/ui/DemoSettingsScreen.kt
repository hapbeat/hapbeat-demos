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
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
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
import com.hapbeat.demoremote.DemoApp
import com.hapbeat.demoremote.DemoCatalog
import com.hapbeat.demoremote.LogState
import com.hapbeat.demoremote.RemoteViewModel

/**
 * The per-demo settings of [demoId] on the selected HMD (demo-session.md「デモごとの設定」): the values read from its Hub
 * (DEMO_SETTINGS_GET) with the phone's unwritten changes over them, the session options, then under 「この Quest の設定」 the
 * `scope: device` ones. 保存 keeps the change on the phone and writes it to the Hub (DEMO_SETTINGS_SET) when the Hub is in
 * front, otherwise it stays 未書き込み. 起動 starts this one demo through the Hub with the Hub's saved settings (HUB_START).
 */
@Composable
fun DemoSettingsScreen(vm: RemoteViewModel, demoId: String, onDone: () -> Unit) {
    val hubDemos = vm.selectedHubDemos
    val label = DemoCatalog.labelFor(demoId, hubDemos)
    val app = DemoCatalog.sessionCandidates(hubDemos).firstOrNull { it.demoId == demoId } ?: DemoApp(demoId, label, packageName = "")
    val options = DemoCatalog.optionsFor(demoId)
    val defaults = options.associate { it.id to it.default }
    val entry = vm.demoSettingsEntry(demoId)
    val stored = defaults + entry?.values.orEmpty()
    // A new read or save starts the draft again from the phone's copy.
    var draft by remember(entry) { mutableStateOf(stored) }
    LaunchedEffect(demoId) {
        vm.clearDemoSettingsStatus()
        // The reasons below come from the last STATE: ask for a fresh one now.
        vm.refreshHubState()
        vm.readDemoSettings(demoId)
    }
    val shown = defaults + draft
    val active = DemoCatalog.activeOptionsFor(demoId, shown)
    val changed = shown != stored
    val unwritten = entry?.unwritten.orEmpty()
    val block = vm.hubPresetBlockReason
    val quest = vm.selectedQuest
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(
            Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                TextButton(onClick = onDone) { Text("← 戻る") }
                Text("$label の設定", style = MaterialTheme.typography.titleLarge, modifier = Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis)
                // Fixed width so the label swap does not shift the heading.
                TextButton(
                    onClick = { vm.readDemoSettings(demoId) },
                    enabled = !vm.demoSettingsReading && quest != null,
                    modifier = Modifier.width(96.dp),
                ) { Text(if (vm.demoSettingsReading) "読込中" else "再読込") }
            }
            Text(
                "${quest?.label ?: "-"} の Hub に保存します（HMD ごとに別です）。保存した設定は、プリセット・Hub のタイル・リモコンのどこから起動しても使われます",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            // Two fixed lines: when the settings were read (or why not), then whether a change is only on the phone.
            Column(Modifier.fillMaxWidth()) {
                val (readText, readError) = vm.demoSettingsStatus
                Text(
                    readText.ifEmpty { if (entry == null) "Hub が起動していると読み込みます" else " " },
                    style = MaterialTheme.typography.bodySmall,
                    color = if (readError) StatusRed else MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    if (unwritten.isNotEmpty()) "未書き込み（スマホだけに保存）: ${demoSettingsSummary(demoId, unwritten)}" else " ",
                    style = MaterialTheme.typography.bodySmall,
                    color = StatusYellow,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            HubManageNotice(vm)
            if (options.isEmpty()) {
                Text("設定できる項目がありません", style = MaterialTheme.typography.bodyMedium)
            }
            OptionChooser(active.filter { !it.device }, shown) { draft = it }
            val device = active.filter { it.device }
            if (device.isNotEmpty()) {
                Text("この Quest の設定", style = MaterialTheme.typography.titleSmall, modifier = Modifier.padding(top = 4.dp))
                OptionChooser(device, shown) { draft = it }
            }
            val startBlock = if (vm.sessionStarting) null else vm.sessionStartBlockReason(app)
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                ActionButton(
                    if (vm.demoSettingsSaving) "保存中…" else "保存",
                    enabled = quest != null && options.isNotEmpty() && !vm.demoSettingsSaving && (changed || unwritten.isNotEmpty()),
                    modifier = Modifier.weight(1f),
                ) { vm.saveDemoSettings(demoId, shown) }
                ActionButton(
                    if (vm.sessionStarting) "起動中…" else "起動",
                    enabled = startBlock == null && !vm.sessionStarting,
                    modifier = Modifier.weight(1f),
                    outlined = true,
                ) { vm.startSession(demoId) }
            }
            // Fixed two lines right under the buttons: the save result, else why a button is gray or what 起動 uses.
            val save = vm.demoSettingsSaveStatus
            val (status, statusColor) = when {
                save != null -> save.first to when (save.second) {
                    LogState.READY -> StatusGreen
                    LogState.SENT, LogState.ACK -> MaterialTheme.colorScheme.onSurfaceVariant
                    else -> StatusRed
                }
                changed -> "保存していない変更は「起動」に使われません" to MaterialTheme.colorScheme.onSurfaceVariant
                startBlock != null -> "起動できません: $startBlock" to MaterialTheme.colorScheme.onSurfaceVariant
                unwritten.isNotEmpty() && block != null -> "Hub に書き込めません: $block" to StatusRed
                else -> " " to MaterialTheme.colorScheme.onSurfaceVariant
            }
            Text(
                status, Modifier.fillMaxWidth().height(40.dp), style = MaterialTheme.typography.bodySmall, color = statusColor,
                maxLines = 2, overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

/** "触覚の強さ: -4 dB、ラウンド: 30秒" for [settings] of [demoId] (labels from this app's table). */
fun demoSettingsSummary(demoId: String, settings: Map<String, String>): String =
    DemoCatalog.optionsFor(demoId).filter { it.id in settings }.joinToString("、") { option ->
        "${option.label}: ${option.values.firstOrNull { it.value == settings[option.id] }?.label ?: settings[option.id]}"
    }
