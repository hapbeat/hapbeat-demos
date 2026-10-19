package com.hapbeat.demoremote.ui

import android.Manifest
import android.content.pm.PackageManager
import android.view.SurfaceHolder
import android.view.SurfaceView
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Tab
import androidx.compose.material3.TabRow
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import com.hapbeat.demoremote.AdbState
import com.hapbeat.demoremote.ControlCatalog
import com.hapbeat.demoremote.DemoCatalog
import com.hapbeat.demoremote.LogEntry
import com.hapbeat.demoremote.LogState
import com.hapbeat.demoremote.PortAction
import com.hapbeat.demoremote.QuestState
import com.hapbeat.demoremote.RemoteViewModel
import com.hapbeat.demoremote.adb.BoundaryState
import com.hapbeat.demoremote.adb.QuestBoundary
import com.hapbeat.demoremote.data.HubDemo
import com.hapbeat.demoremote.data.HubPresets
import com.hapbeat.demoremote.data.HubPresetSlot
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions

/**
 * [tab]: the selected tab (起動 / 切替 / 操作 / デモ), kept by the caller so that it survives opening another screen.
 * [onOpenDemoSettings]: a tile of the デモ tab opens that demo's per-demo settings.
 */
@Composable
fun RemoteScreen(
    vm: RemoteViewModel, onOpenSettings: () -> Unit, onEditPreset: (Int) -> Unit = {}, onOpenHubSettings: () -> Unit = {},
    tab: Int = 0, onTab: (Int) -> Unit = {}, onOpenDemoSettings: (String) -> Unit = {},
) {
    if (!vm.authChosen) AuthChoiceDialog(vm)
    if (vm.sleepHintIp != null) SleepHintDialog(vm)
    val wide = LocalConfiguration.current.screenWidthDp >= 600
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        if (wide) {
            Row(Modifier.fillMaxSize().padding(12.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Column(Modifier.weight(1.5f).fillMaxHeight()) {
                    MirrorHeader(vm)
                    if (vm.mirrorEnabled) MirrorPane(vm, Modifier.fillMaxWidth().weight(1f))
                }
                Column(
                    Modifier.weight(1f).fillMaxHeight().verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    QuestBar(vm, onOpenSettings)
                    ActionsAndLog(vm, onEditPreset, onOpenHubSettings, tab, onTab, onOpenDemoSettings)
                }
            }
        } else {
            // Phone portrait: the mirror stays pinned at the top while the controls below scroll.
            val mirrorHeight = (LocalConfiguration.current.screenHeightDp * 0.36f).dp
            Column(Modifier.fillMaxSize().padding(horizontal = 12.dp)) {
                MirrorHeader(vm)
                // ON opens the pane, OFF closes it: no separate collapse button.
                if (vm.mirrorEnabled) MirrorPane(vm, Modifier.fillMaxWidth().height(mirrorHeight))
                Column(
                    Modifier.fillMaxWidth().weight(1f).verticalScroll(rememberScrollState()).padding(vertical = 8.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    QuestBar(vm, onOpenSettings)
                    ActionsAndLog(vm, onEditPreset, onOpenHubSettings, tab, onTab, onOpenDemoSettings)
                }
            }
        }
    }
}

@Composable
private fun ActionsAndLog(
    vm: RemoteViewModel, onEditPreset: (Int) -> Unit, onOpenHubSettings: () -> Unit, tab: Int, onTab: (Int) -> Unit,
    onOpenDemoSettings: (String) -> Unit,
) {
    val quest = vm.selectedQuest
    val hubInstalled = quest?.installed?.contains(DemoCatalog.hub.packageName) != false
    // The two most urgent actions stay one tap away: back to the Hub, and pause (shared pause panel).
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        ActionButton(
            "Hub を開く",
            enabled = quest?.adb == AdbState.CONNECTED && hubInstalled,
            modifier = Modifier.weight(1f).height(64.dp),
        ) { vm.launchApp(DemoCatalog.hub) }
        ActionButton(
            "メニューを開く",
            enabled = vm.demoSwitchBlockReason == null,
            modifier = Modifier.weight(1f).height(64.dp),
        ) { vm.sendControl(ControlCatalog.menuOpen) }
    }
    NoticeArea(vm)
    TabRow(selectedTabIndex = tab) {
        listOf("起動", "切替", "操作", "デモ").forEachIndexed { i, title ->
            Tab(selected = tab == i, onClick = { onTab(i) }, text = { Text(title) })
        }
    }
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        when (tab) {
            0 -> SessionTab(vm, onEditPreset, onOpenHubSettings)
            1 -> SwitchTab(vm)
            2 -> ControlTab(vm)
            else -> DemoTab(vm, onOpenDemoSettings)
        }
    }
    Text("結果ログ", style = MaterialTheme.typography.titleSmall)
    LogArea(vm.logs)
}

// ---- Quest selection ---------------------------------------------------------------------------

@Composable
private fun QuestBar(vm: RemoteViewModel, onOpenSettings: () -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(Modifier.weight(1f).horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            // Responders inside a development editor are left out unless settings ask for them (STATE `editor`).
            val listed = vm.listedQuests
            if (listed.isEmpty()) Text("Quest が見つかっていません", Modifier.padding(vertical = 16.dp))
            listed.forEach { q ->
                FilterChip(
                    selected = q.ip == vm.selectedIp,
                    onClick = { vm.selectQuest(q.ip) },
                    label = { Text(q.label) },
                    // Green: Demo Switch receiver answered. Yellow: only Wi-Fi adb is reachable (home screen etc.).
                    leadingIcon = {
                        StatusDot(when {
                            q.respondedLastRound == true -> StatusGreen
                            q.adbPortOpen == true || q.adb == AdbState.CONNECTED -> StatusYellow
                            else -> StatusGray
                        })
                    },
                    modifier = Modifier.height(48.dp),
                )
            }
        }
        // Fixed width so the label swap does not shift the chips.
        val searching = vm.discovering || vm.scanningAdb
        TextButton(onClick = vm::rediscover, enabled = !searching, modifier = Modifier.width(96.dp)) {
            Text(if (searching) "探索中" else "再探索")
        }
        TextButton(onClick = onOpenSettings) { Text("設定") }
    }
    QuestStatus(vm, vm.selectedQuest)
    DiagnosticsLines(vm)
}

/**
 * Fixed two small lines for on-site diagnosis: the phone's Wi-Fi (and the Quest's, once read over USB),
 * then the last discovery / 5555 scan.
 */
@Composable
private fun DiagnosticsLines(vm: RemoteViewModel) {
    val wifi = vm.wifiDiag + if (vm.usbQuestDiag.isEmpty()) "" else "　／ ${vm.usbQuestDiag}"
    // labelSmall (11sp / line 16sp): a bare fontSize would keep bodyLarge's 24sp line height. One line each = fixed height.
    Column(Modifier.fillMaxWidth().padding(horizontal = 8.dp)) {
        listOf(wifi, vm.discoveryDiag).forEach {
            Text(it, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
private fun QuestStatus(vm: RemoteViewModel, quest: QuestState?) {
    Column(Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surfaceVariant).padding(8.dp)) {
        if (quest == null) {
            Text("宛先の Quest を上の一覧から選んでください", style = MaterialTheme.typography.bodyMedium)
            Text(" ", style = MaterialTheme.typography.bodySmall)
            return@Column
        }
        Text(
            "${quest.label}  ${quest.ip}${if (quest.model.isNotEmpty()) "  (${quest.model})" else ""}",
            style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
            // "デモ応答 ○" means running; STATE tells whether it is actually in front (commands are refused otherwise).
            val front = if (quest.lastDemoId.isEmpty()) "-" else DemoCatalog.labelFor(quest.lastDemoId) +
                if (quest.remoteState?.foreground == false) "（非前面: メニュー・一時停止・境界設定など）" else ""
            Text("前面: $front", Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text("デモ応答 ${mark(quest.respondedLastRound)}")
            Text("adb ${if (quest.adb == AdbState.CONNECTED) "○" else "×"}")
            val battery = quest.battery
            Text(
                if (battery == null) "電池 --%" else "電池 $battery%",
                color = if (battery != null && battery <= 20) StatusRed else Color.Unspecified,
                maxLines = 1,
                modifier = Modifier.width(104.dp),
            )
            // Fixed width: "なし / あり / 不明" never moves the items on its left.
            val boundary = if (quest.adb == AdbState.CONNECTED) quest.boundary else BoundaryState.UNKNOWN
            Text("境界: ${QuestBoundary.label(boundary)}", maxLines = 1, modifier = Modifier.width(80.dp))
        }
        // Read-only state from adb (developer mode / adb / uptime / build); one fixed line.
        Text(
            quest.info.ifEmpty { "-" },
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        // adb status / guidance: fixed three-line area so messages never shift the layout.
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                vm.adbMessage.ifEmpty { adbStateText(quest.adb) },
                Modifier.weight(1f).height(56.dp),
                style = MaterialTheme.typography.bodySmall,
                maxLines = 3,
                overflow = TextOverflow.Ellipsis,
                color = when {
                    vm.adbMessage.isEmpty() || quest.adb != AdbState.DISCONNECTED -> Color.Unspecified
                    vm.adbMessageError -> StatusRed
                    else -> MaterialTheme.colorScheme.onSurfaceVariant
                },
            )
            val connecting = quest.adb == AdbState.CONNECTING || quest.adb == AdbState.AUTH_WAIT
            // Wi-Fi adb off (port 5555 closed): the button leads to the USB re-enable dialog instead.
            val usbFix = quest.adb == AdbState.DISCONNECTED && vm.wifiAdbOff
            TextButton(
                onClick = { if (connecting) vm.cancelAdbConnect() else if (usbFix) vm.openUsbDialog() else vm.connectAdb() },
                modifier = Modifier.width(128.dp),
            ) {
                Text(
                    when {
                        connecting -> "中止"
                        usbFix -> "USB でつなぐ"
                        quest.adb == AdbState.CONNECTED -> "繋ぎ直す"
                        else -> "adb 接続"
                    },
                    fontWeight = if (usbFix) FontWeight.Bold else null,
                    maxLines = 1,
                )
            }
        }
        // Why the known demo in front does not answer Demo Switch (adb diagnosis) and its fix: fixed two-line area.
        Row(Modifier.fillMaxWidth().height(48.dp), verticalAlignment = Alignment.CenterVertically) {
            val (text, error) = vm.portStatus
            Text(
                text.ifEmpty { " " },
                Modifier.weight(1f),
                style = MaterialTheme.typography.bodySmall,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
                color = if (error) StatusRed else MaterialTheme.colorScheme.onSurfaceVariant,
            )
            // Fixed width whether or not a button is offered, so the text does not move.
            Box(Modifier.width(128.dp)) {
                if (vm.portAction != PortAction.NONE) {
                    TextButton(
                        onClick = { if (vm.portAction == PortAction.STOP_BACKGROUND) vm.stopBackgroundDemos() else vm.restartFrontApp() },
                        enabled = !vm.portActionRunning && quest.adb == AdbState.CONNECTED,
                        modifier = Modifier.width(128.dp),
                    ) {
                        Text(
                            if (vm.portAction == PortAction.STOP_BACKGROUND) "裏のデモを止める" else "前面のアプリを再起動",
                            style = MaterialTheme.typography.labelMedium,
                            fontWeight = FontWeight.Bold,
                            maxLines = 2,
                        )
                    }
                }
            }
        }
    }
}

private fun mark(value: Boolean?): String = when (value) { true -> "○"; false -> "×"; null -> "-" }

private fun adbStateText(state: AdbState): String = when (state) {
    AdbState.DISCONNECTED -> "adb 未接続"
    AdbState.CONNECTING -> "adb 接続中…"
    AdbState.AUTH_WAIT -> "ヘッドセット内で許可してください"
    AdbState.CONNECTED -> "adb 接続済み"
}

@Composable
private fun StatusDot(color: Color) {
    Box(Modifier.size(10.dp).background(color, CircleShape))
}

// ---- notice / tabs -------------------------------------------------------------------------

/** Fixed two-line area. A low HMD battery takes the first line of its own, so another notice cannot hide it. */
@Composable
private fun NoticeArea(vm: RemoteViewModel) {
    val reason = vm.demoSwitchBlockReason
    val battery = vm.selectedQuest?.battery
    val (text, error) = if (reason != null) reason to true else vm.notice
    val color = if (error) StatusRed else MaterialTheme.colorScheme.onSurfaceVariant
    Column(Modifier.fillMaxWidth().height(40.dp)) {
        if (battery != null && battery <= 20) {
            Text("HMD の電池が少なくなっています（$battery%）", style = MaterialTheme.typography.bodySmall, color = StatusRed, maxLines = 1)
            Text(text, style = MaterialTheme.typography.bodySmall, color = color, maxLines = 1, overflow = TextOverflow.Ellipsis)
        } else {
            Text(text, style = MaterialTheme.typography.bodySmall, color = color, maxLines = 2, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
private fun SwitchTab(vm: RemoteViewModel) {
    val enabled = vm.demoSwitchBlockReason == null
    val current = vm.selectedQuest?.lastDemoId
    Text(
        "前面のデモに次のデモへの切替を頼みます（前面に Demo Switch 対応デモが必要）。Hub のチュートリアル付きセッションにはなりません",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
    ButtonGrid(DemoCatalog.apps, columns = 3) { app, modifier ->
        DemoTile(app, enabled = enabled && app.demoId != current, modifier = modifier) { vm.sendSwitch(app.demoId) }
    }
}

@Composable
private fun ControlTab(vm: RemoteViewModel) {
    val enabled = vm.demoSwitchBlockReason == null
    val current = vm.selectedQuest?.lastDemoId
    Text(
        "送信前に前面アプリを確認し、そのアプリへ送ります",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
    StateSummary(vm.selectedQuest)
    val state = vm.selectedQuest?.remoteState
    ControlCatalog.groups.forEach { group ->
        Text(group.title, style = MaterialTheme.typography.titleSmall, modifier = Modifier.padding(top = 4.dp))
        ButtonGrid(group.actions, columns = 2) { control, modifier ->
            val allowed = enabled && (control.demoId == null || control.demoId == current) &&
                ControlCatalog.unavailableReason(control, state) == null
            // The current hand look is the filled one (same size, so nothing moves).
            val currentLook = state?.handStyle != null && control.action == "hand_style_${state.handStyle}"
            ActionButton(control.label, enabled = allowed, modifier = modifier, outlined = !currentLook) { vm.sendControl(control) }
        }
        if (group.stateful) {
            // Fixed one line: why a button of this group is gray (from the last STATE), else blank.
            Text(
                group.actions.firstNotNullOfOrNull { ControlCatalog.unavailableReason(it, state) } ?: " ",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        // The boundary goes over adb, not Demo Switch: its own group next to the Hub one.
        if (group.title == "Hub") BoundaryGroup(vm)
    }
}

/** 「境界を止める／戻す」 over adb, run at once; a fixed line under it says why they are gray. */
@Composable
private fun BoundaryGroup(vm: RemoteViewModel) {
    val block = vm.boundaryBlockReason
    Text("境界", style = MaterialTheme.typography.titleSmall, modifier = Modifier.padding(top = 4.dp))
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        val enabled = block == null && !vm.boundaryRunning
        ActionButton("境界を止める", enabled = enabled, modifier = Modifier.weight(1f)) { vm.setBoundaryPaused(true) }
        ActionButton("境界を戻す", enabled = enabled, modifier = Modifier.weight(1f), outlined = true) { vm.setBoundaryPaused(false) }
    }
    Text(
        block ?: " ",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        maxLines = 1,
        overflow = TextOverflow.Ellipsis,
    )
}

/** Current state from STATE (QUERY); falls back to the last successful haptics operation from this app. */
@Composable
private fun StateSummary(quest: QuestState?) {
    val state = quest?.remoteState
    val onOff = { v: Boolean -> if (v) "ON" else "OFF" }
    val shown = { v: Boolean -> if (v) "表示" else "非表示" }
    val lines = if (state != null) {
        listOf(
            "触覚 ${onOff(state.hapticsOn)}　触覚ボタン ${shown(state.hapticsUi)}　リセットボタン ${shown(state.recenterUi)}",
            (if (state.foreground) "" else "非前面　") + (if (state.paused) "一時停止中" else "進行中") +
                (if (state.stepCount > 0) "　セッション ${state.stepIndex + 1}/${state.stepCount}" else "　セッション外"),
        )
    } else {
        val haptics = when (quest?.hapticsOn) { true -> "ON"; false -> "OFF"; null -> "不明" }
        listOf("触覚 $haptics（このアプリから最後に成功した操作）", "状態問い合わせ（QUERY）に未対応か応答なし")
    }
    // Fixed two-line block: the text changes, the layout does not.
    Column(Modifier.fillMaxWidth().height(44.dp)) {
        lines.forEach { Text(it, style = MaterialTheme.typography.bodySmall, maxLines = 1, overflow = TextOverflow.Ellipsis) }
    }
}

@Composable
private fun SessionTab(vm: RemoteViewModel, onEditPreset: (Int) -> Unit, onOpenHubSettings: () -> Unit) {
    val quest = vm.selectedQuest
    // Hub presets live on the selected HMD's Hub and go over Demo Switch (no adb).
    val block = vm.hubPresetBlockReason
    val slots = vm.selectedHubPresets
    val hubDemos = vm.selectedHubDemos
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text("プリセット（Hub に保存）", style = MaterialTheme.typography.titleSmall, modifier = Modifier.weight(1f))
        TextButton(onClick = onOpenHubSettings, enabled = quest != null, modifier = Modifier.width(112.dp)) { Text("Hub の設定") }
        // Fixed width so the label swap does not shift the heading.
        TextButton(
            onClick = vm::reloadHubPresets,
            enabled = !vm.hubPresetReading && quest?.remoteState?.currentDemoId == DemoCatalog.HUB_ID,
            modifier = Modifier.width(96.dp),
        ) { Text(if (vm.hubPresetReading) "読込中" else "再読込") }
    }
    // Two fixed lines: why editing / starting is off (else when the presets were read), then whether the slots below
    // are only the last read content (gray) while the Hub is not in front.
    val stale = block != null && slots.any { it != null }
    Column(Modifier.fillMaxWidth()) {
        Text(
            block ?: vm.hubPresetStatus.ifEmpty { " " },
            style = MaterialTheme.typography.bodySmall,
            color = if (block != null) StatusRed else MaterialTheme.colorScheme.onSurfaceVariant,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        Text(
            if (stale) "下は最後に読んだ内容です" else " ",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            maxLines = 1,
        )
    }
    var confirmStart by remember { mutableStateOf<Int?>(null) }
    slots.forEachIndexed { i, slot ->
        HubPresetRow(i + 1, slot, hubDemos, stale = block != null, canWrite = block == null, onEdit = { onEditPreset(i + 1) }, onStart = { confirmStart = i + 1 })
    }
    QrImportButton(vm, Modifier.fillMaxWidth())
    // Started by the Hub from what it stores: show that content once more before starting.
    confirmStart?.let { number ->
        val slot = slots.getOrNull(number - 1)
        AlertDialog(
            onDismissRequest = { confirmStart = null },
            title = { Text("プリセット $number で始めますか？") },
            text = {
                Column(Modifier.heightIn(max = 360.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text("${quest?.label ?: "-"}：${slot?.let { presetTitle(it) } ?: "-"}", fontWeight = FontWeight.Bold)
                    slot?.preset?.steps?.forEachIndexed { n, step -> Text("${n + 1}. ${stepSummary(step, hubDemos)}", style = MaterialTheme.typography.bodySmall) }
                    if (slot != null && slot.unreadable) {
                        Text("${slot.preset.steps.size + 1} 本目以降は読めません（全 ${slot.stepCount} 本）", style = MaterialTheme.typography.bodySmall)
                    }
                }
            },
            confirmButton = { TextButton(onClick = { confirmStart = null; vm.startHubPreset(number) }) { Text("始める") } },
            dismissButton = { TextButton(onClick = { confirmStart = null }) { Text("やめる") } },
        )
    }
    // One demo alone starts from the デモ tab (its settings screen's 「起動」).
    // At the end of the tab: rows added or removed here never move anything above.
    UnwrittenPresetSection(vm, block, hubDemos)
}

/**
 * Presets imported from a QR / link and kept on the phone (the Hub was not in front): 確認 (content and slot), 削除 and
 * Hub に書き込む (PRESET_SET, only while the Hub is in front). The heading and the status line are always there.
 */
@Composable
private fun UnwrittenPresetSection(vm: RemoteViewModel, block: String?, hubDemos: List<HubDemo>?) {
    Text("未書き込みのプリセット（${vm.unwrittenPresets.size} 件）", style = MaterialTheme.typography.titleSmall)
    Text(
        vm.unwrittenStatus.first.ifEmpty {
            if (vm.unwrittenPresets.isEmpty()) "QR から取り込んで Hub に書けなかったものが、ここに残ります"
            else block?.let { "Hub に書き込めません: $it" } ?: " "
        },
        style = MaterialTheme.typography.bodySmall,
        color = if (vm.unwrittenStatus.second) StatusRed else MaterialTheme.colorScheme.onSurfaceVariant,
        maxLines = 1,
        overflow = TextOverflow.Ellipsis,
    )
    var shown by remember { mutableStateOf<Long?>(null) }
    var deleting by remember { mutableStateOf<Long?>(null) }
    vm.unwrittenPresets.forEach { item ->
        Row(
            Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surfaceVariant).padding(start = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column(Modifier.weight(1f).padding(vertical = 4.dp)) {
                Text(item.preset.name, style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text("→ プリセット ${item.slot}・${stepsSummary(item.preset.steps, hubDemos)}", style = MaterialTheme.typography.bodySmall,
                    maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            TextButton(onClick = { shown = item.id }, modifier = Modifier.width(56.dp)) { Text("確認") }
            TextButton(onClick = { deleting = item.id }, enabled = vm.unwrittenWriting != item.id, modifier = Modifier.width(56.dp)) { Text("削除") }
            ActionButton(
                if (vm.unwrittenWriting == item.id) "書込中" else "Hub に書き込む",
                enabled = block == null && vm.unwrittenWriting == null && !vm.importRunning,
                modifier = Modifier.width(112.dp),
            ) { vm.writeUnwritten(item.id) }
        }
    }
    vm.unwrittenPresets.firstOrNull { it.id == shown }?.let { item ->
        val slots = vm.selectedHubPresets
        AlertDialog(
            onDismissRequest = { shown = null },
            title = { Text(item.preset.name, maxLines = 2, overflow = TextOverflow.Ellipsis) },
            text = {
                Column(Modifier.heightIn(max = 420.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    item.preset.steps.forEachIndexed { n, step -> Text("${n + 1}. ${stepSummary(step, hubDemos)}", style = MaterialTheme.typography.bodySmall) }
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                        Text("書き込む枠", style = MaterialTheme.typography.bodySmall)
                        HubPresets.NUMBERS.forEach { number ->
                            FilterChip(selected = item.slot == number, enabled = vm.unwrittenWriting != item.id,
                                onClick = { vm.setUnwrittenSlot(item.id, number) }, label = { Text("$number") })
                        }
                    }
                    // Fixed line: what is in the chosen slot now.
                    val (text, overwrite) = slotNote(item.slot, slots.getOrNull(item.slot - 1))
                    Text(text, style = MaterialTheme.typography.bodySmall, color = if (overwrite) StatusRed else MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            },
            confirmButton = { TextButton(onClick = { shown = null }) { Text("閉じる") } },
        )
    }
    vm.unwrittenPresets.firstOrNull { it.id == deleting }?.let { item ->
        AlertDialog(
            onDismissRequest = { deleting = null },
            title = { Text("未書き込みのプリセットを削除しますか？") },
            text = { Text("「${item.preset.name}」をスマホから消します（Hub のプリセットは変わりません）") },
            confirmButton = { TextButton(onClick = { deleting = null; vm.deleteUnwritten(item.id) }) { Text("削除") } },
            dismissButton = { TextButton(onClick = { deleting = null }) { Text("やめる") } },
        )
    }
}

/**
 * A user-started Wi-Fi adb connect gets no answer (the Quest's Wi-Fi is likely in power save). The connect keeps trying
 * while this is up and closes it when it ends; 閉じる only hides it.
 */
@Composable
private fun SleepHintDialog(vm: RemoteViewModel) {
    AlertDialog(
        onDismissRequest = vm::dismissSleepHint,
        title = { Text("Quest が寝ているかもしれません") },
        text = { Text("Quest の電源ボタンを短く押して画面をつけるか、ヘッドセットを被ってください。起きると自動でつながります。") },
        confirmButton = { TextButton(onClick = vm::dismissSleepHint) { Text("閉じる") } },
    )
}

/**
 * One Hub slot: number, name, whether the Hub shows it, the demo order (options and retry), 編集 and 開始.
 * [stale]: the Hub is not in front, so this is the last read content (gray). Fixed height: the text changes only.
 */
@Composable
private fun HubPresetRow(
    number: Int, slot: HubPresetSlot?, hubDemos: List<HubDemo>?, stale: Boolean, canWrite: Boolean, onEdit: () -> Unit, onStart: () -> Unit,
) {
    val color = if (stale) MaterialTheme.colorScheme.onSurface.copy(alpha = 0.45f) else Color.Unspecified
    Row(
        Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surfaceVariant).padding(start = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.weight(1f).padding(vertical = 4.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    "$number  ${slot?.let { presetTitle(it) } ?: "未読込"}",
                    Modifier.weight(1f), style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold, color = color,
                    maxLines = 1, overflow = TextOverflow.Ellipsis,
                )
                Text(
                    when {
                        slot == null || slot.stepCount == 0 -> ""
                        slot.preset.visible -> "Hub に表示"
                        else -> "Hub に非表示"
                    },
                    style = MaterialTheme.typography.bodySmall, color = color, maxLines = 1,
                )
            }
            Text(
                when {
                    slot == null -> "Hub を開くと読み込みます"
                    slot.unreadable -> "大きすぎて読めないデモがあります（Hub で編集してください）"
                    slot.stepCount == 0 -> "「編集」でデモを選んで作れます"
                    else -> stepsSummary(slot.preset.steps, hubDemos)
                },
                style = MaterialTheme.typography.bodySmall, color = color, minLines = 2, maxLines = 2, overflow = TextOverflow.Ellipsis,
            )
        }
        TextButton(onClick = onEdit, enabled = canWrite && slot != null && !slot.unreadable, modifier = Modifier.width(64.dp)) { Text("編集") }
        ActionButton("開始", enabled = canWrite && slot != null && slot.stepCount > 0, modifier = Modifier.width(88.dp), onClick = onStart)
    }
}

/** Name of a read slot: "空き" when it has no demos and no name, "（名前なし）" when only the name is empty. */
private fun presetTitle(slot: HubPresetSlot): String = when {
    slot.preset.name.isNotEmpty() -> slot.preset.name
    slot.stepCount == 0 -> "空き"
    else -> "（名前なし）"
}

/**
 * Reads a showcase QR with the in-app scanner (zxing-android-embedded). The camera permission is asked
 * right before the first scan; the result goes to the import confirmation dialog.
 */
@Composable
private fun QrImportButton(vm: RemoteViewModel, modifier: Modifier) {
    val context = LocalContext.current
    val scan = rememberLauncherForActivityResult(ScanContract()) { result ->
        result.contents?.let(vm::importFromQr)
    }
    val scanOptions = {
        ScanOptions()
            .setDesiredBarcodeFormats(ScanOptions.QR_CODE)
            .setPrompt("ショーケースのプリセットの QR を枠に入れてください")
            .setBeepEnabled(false)
            .setOrientationLocked(false)
    }
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) scan.launch(scanOptions()) else vm.onQrCameraDenied()
    }
    TextButton(
        onClick = {
            if (ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) {
                scan.launch(scanOptions())
            } else {
                permission.launch(Manifest.permission.CAMERA)
            }
        },
        modifier = modifier,
    ) { Text("QR から Hub に取り込む") }
}

/**
 * The selected HMD's demos (the Hub's installed ones once read); a tile opens that demo's per-demo settings (保存 / 起動).
 * Below, the changes saved on the phone that the Hub does not have yet, each with 書き込み.
 */
@Composable
private fun DemoTab(vm: RemoteViewModel, onOpenDemoSettings: (String) -> Unit) {
    val quest = vm.selectedQuest
    val hubDemos = vm.selectedHubDemos
    Text(
        "デモを押すと、この Quest でのデモごとの設定を開きます（Hub に保存。プリセット・タイル・リモコンのどこから起動しても使われます）",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
    ButtonGrid(DemoCatalog.sessionCandidates(hubDemos), columns = 3) { app, modifier ->
        DemoTile(app, enabled = quest != null, modifier = modifier, outlined = true) { onOpenDemoSettings(app.demoId) }
    }
    UnwrittenDemoSettingsSection(vm, hubDemos, onOpenDemoSettings)
}

/** The selected HMD's unwritten per-demo changes: the heading and the status line are always there. */
@Composable
private fun UnwrittenDemoSettingsSection(vm: RemoteViewModel, hubDemos: List<HubDemo>?, onOpenDemoSettings: (String) -> Unit) {
    val items = vm.selectedUnwrittenDemoSettings
    val block = vm.hubPresetBlockReason
    Text("未書き込みのデモの設定（${items.size} 件）", style = MaterialTheme.typography.titleSmall)
    val save = vm.demoSettingsSaveStatus
    Text(
        when {
            save != null -> save.first
            items.isEmpty() -> "Hub が前面でないときに保存した設定が、ここに残ります"
            block != null -> "Hub に書き込めません: $block"
            else -> " "
        },
        style = MaterialTheme.typography.bodySmall,
        color = when (save?.second) {
            null, LogState.SENT, LogState.ACK -> MaterialTheme.colorScheme.onSurfaceVariant
            LogState.READY -> StatusGreen
            else -> StatusRed
        },
        maxLines = 1,
        overflow = TextOverflow.Ellipsis,
    )
    items.forEach { item ->
        Row(
            Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surfaceVariant).padding(start = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column(Modifier.weight(1f).padding(vertical = 4.dp)) {
                Text(DemoCatalog.labelFor(item.demoId, hubDemos), style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold, maxLines = 1)
                Text(demoSettingsSummary(item.demoId, item.settings), style = MaterialTheme.typography.bodySmall, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            TextButton(onClick = { onOpenDemoSettings(item.demoId) }, modifier = Modifier.width(56.dp)) { Text("確認") }
            ActionButton(
                if (vm.demoSettingsSaving) "書込中" else "Hub に書き込む",
                enabled = block == null && !vm.demoSettingsSaving,
                modifier = Modifier.width(112.dp),
            ) { vm.writeDemoSettings(item.demoId) }
        }
    }
}

// ---- log ------------------------------------------------------------------------------------

@Composable
private fun LogArea(logs: List<LogEntry>) {
    Box(Modifier.fillMaxWidth().height(220.dp).background(MaterialTheme.colorScheme.surfaceVariant)) {
        if (logs.isEmpty()) {
            Text("まだ送信していません", Modifier.padding(8.dp), style = MaterialTheme.typography.bodySmall)
        }
        LazyColumn(Modifier.fillMaxSize().padding(horizontal = 8.dp, vertical = 4.dp)) {
            items(logs, key = { it.id }) { entry -> LogRow(entry) }
        }
    }
}

@Composable
private fun LogRow(entry: LogEntry) {
    val color = when (entry.state) {
        LogState.ACK -> StatusYellow
        LogState.READY -> StatusGreen
        LogState.FAILED, LogState.ERROR -> StatusRed
        LogState.NO_RESPONSE -> StatusGray
        LogState.SENT, LogState.INFO -> MaterialTheme.colorScheme.onSurfaceVariant
    }
    Row(Modifier.fillMaxWidth().padding(vertical = 3.dp), verticalAlignment = Alignment.Top) {
        StatusDot(color)
        Spacer(Modifier.width(6.dp))
        Column {
            Text(
                "${entry.time}  ${entry.target}  ${entry.content}${entry.seq?.let { "  #$it" } ?: ""}",
                fontSize = 13.sp,
            )
            if (entry.detail.isNotEmpty()) Text(entry.detail, fontSize = 12.sp, color = color)
        }
    }
    HorizontalDivider()
}

// ---- mirror ---------------------------------------------------------------------------------

@Composable
private fun MirrorHeader(vm: RemoteViewModel) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text("画面ミラー", style = MaterialTheme.typography.titleSmall, modifier = Modifier.weight(1f))
        // Fixed width whether or not the retry button is shown.
        Box(Modifier.width(88.dp)) {
            if (vm.mirrorEnabled && vm.mirrorStatus.startsWith("ミラーエラー")) TextButton(onClick = vm::retryMirror) { Text("再試行") }
        }
        Switch(checked = vm.mirrorEnabled, onCheckedChange = vm::changeMirrorEnabled)
    }
}

@Composable
private fun MirrorPane(vm: RemoteViewModel, modifier: Modifier) {
    Box(modifier.background(Color.Black), contentAlignment = Alignment.Center) {
        if (vm.mirrorEnabled) {
            val size = vm.mirrorVideoSize
            val aspect = if (size != null && size.second > 0) size.first.toFloat() / size.second else 16f / 9f
            AndroidView(
                factory = { context ->
                    SurfaceView(context).apply {
                        holder.addCallback(object : SurfaceHolder.Callback {
                            override fun surfaceCreated(holder: SurfaceHolder) = vm.onMirrorSurfaceAvailable(holder.surface)
                            override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) = Unit
                            override fun surfaceDestroyed(holder: SurfaceHolder) = vm.onMirrorSurfaceDestroyed()
                        })
                    }
                },
                modifier = Modifier.aspectRatio(aspect),
            )
        }
        if (vm.mirrorStatus.isNotEmpty()) {
            Text(vm.mirrorStatus, color = Color.White, style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(12.dp))
        }
    }
}

// ---- first launch ------------------------------------------------------------------------------

@Composable
private fun AuthChoiceDialog(vm: RemoteViewModel) {
    var secret by remember { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = {},
        title = { Text("認証モードを選んでください") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(
                    "現在のデモ受信側はすべて署名なしモードで動いています。署名なしは外部から隔離された" +
                        "デモ専用 LAN でだけ使ってください。受信側に shared secret を設定した場合は同じ値を入力します。",
                    style = MaterialTheme.typography.bodyMedium,
                )
                OutlinedTextField(
                    value = secret,
                    onValueChange = { secret = it },
                    label = { Text("shared secret") },
                    singleLine = true,
                    visualTransformation = PasswordVisualTransformation(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        },
        confirmButton = {
            TextButton(onClick = { vm.setSecret(secret) }, enabled = secret.isNotEmpty()) { Text("secret を使う") }
        },
        dismissButton = {
            TextButton(onClick = { vm.setAllowUnsigned(true) }) { Text("隔離 LAN で署名なしを使う") }
        },
    )
}
