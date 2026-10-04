package com.hapbeat.demoremote.ui

import android.view.SurfaceHolder
import android.view.SurfaceView
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
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import com.hapbeat.demoremote.AdbState
import com.hapbeat.demoremote.ControlCatalog
import com.hapbeat.demoremote.DemoCatalog
import com.hapbeat.demoremote.LogEntry
import com.hapbeat.demoremote.LogState
import com.hapbeat.demoremote.QuestState
import com.hapbeat.demoremote.RemoteViewModel

@Composable
fun RemoteScreen(vm: RemoteViewModel, onOpenSettings: () -> Unit) {
    if (!vm.authChosen) AuthChoiceDialog(vm)
    val wide = LocalConfiguration.current.screenWidthDp >= 600
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        if (wide) {
            Row(Modifier.fillMaxSize().padding(12.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Column(Modifier.weight(1.5f).fillMaxHeight()) {
                    MirrorHeader(vm, collapsed = false, onToggleCollapsed = null)
                    MirrorPane(vm, Modifier.fillMaxWidth().weight(1f))
                }
                Column(
                    Modifier.weight(1f).fillMaxHeight().verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    ControlColumn(vm, onOpenSettings)
                }
            }
        } else {
            var mirrorCollapsed by rememberSaveable { mutableStateOf(false) }
            Column(
                Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(12.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                QuestBar(vm, onOpenSettings)
                MirrorHeader(vm, collapsed = mirrorCollapsed, onToggleCollapsed = { mirrorCollapsed = !mirrorCollapsed })
                if (!mirrorCollapsed) MirrorPane(vm, Modifier.fillMaxWidth().aspectRatio(16f / 9f))
                ActionsAndLog(vm)
            }
        }
    }
}

@Composable
private fun ControlColumn(vm: RemoteViewModel, onOpenSettings: () -> Unit) {
    QuestBar(vm, onOpenSettings)
    ActionsAndLog(vm)
}

@Composable
private fun ActionsAndLog(vm: RemoteViewModel) {
    val quest = vm.selectedQuest
    val hubInstalled = quest?.installed?.contains(DemoCatalog.hub.packageName) != false
    ActionButton(
        "Hub を開く",
        enabled = quest?.adb == AdbState.CONNECTED && hubInstalled,
        modifier = Modifier.fillMaxWidth().height(64.dp),
    ) { vm.launchApp(DemoCatalog.hub) }
    NoticeArea(vm)
    var tab by rememberSaveable { mutableIntStateOf(0) }
    TabRow(selectedTabIndex = tab) {
        listOf("アプリ切替", "アプリ操作", "直接起動").forEachIndexed { i, title ->
            Tab(selected = tab == i, onClick = { tab = i }, text = { Text(title) })
        }
    }
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        when (tab) {
            0 -> SwitchTab(vm)
            1 -> ControlTab(vm)
            else -> LaunchTab(vm)
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
            if (vm.quests.isEmpty()) Text("Quest が見つかっていません", Modifier.padding(vertical = 16.dp))
            vm.quests.forEach { q ->
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
            Text("前面: ${if (quest.lastDemoId.isEmpty()) "-" else DemoCatalog.labelFor(quest.lastDemoId)}", Modifier.weight(1f), maxLines = 1)
            Text("DS ${mark(quest.respondedLastRound)}")
            Text("adb ${if (quest.adb == AdbState.CONNECTED) "○" else "×"}")
            val battery = quest.battery
            Text(
                if (battery == null) "電池 --%" else "電池 $battery%",
                color = if (battery != null && battery <= 20) StatusRed else Color.Unspecified,
                modifier = Modifier.width(72.dp),
            )
        }
        // adb status / guidance: fixed two-line area so messages never shift the layout.
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                vm.adbMessage.ifEmpty { adbStateText(quest.adb) },
                Modifier.weight(1f).height(40.dp),
                style = MaterialTheme.typography.bodySmall,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
                color = if (vm.adbMessage.isNotEmpty() && quest.adb == AdbState.DISCONNECTED) StatusRed else Color.Unspecified,
            )
            val connecting = quest.adb == AdbState.CONNECTING || quest.adb == AdbState.AUTH_WAIT
            TextButton(
                onClick = { if (connecting) vm.cancelAdbConnect() else vm.connectAdb() },
                modifier = Modifier.width(112.dp),
            ) { Text(if (connecting) "中止" else if (quest.adb == AdbState.CONNECTED) "adb 再接続" else "adb 接続") }
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

@Composable
private fun NoticeArea(vm: RemoteViewModel) {
    val reason = vm.demoSwitchBlockReason
    val (text, error) = if (reason != null) reason to true else vm.notice
    Text(
        text,
        Modifier.fillMaxWidth().height(40.dp),
        style = MaterialTheme.typography.bodySmall,
        color = if (error) StatusRed else MaterialTheme.colorScheme.onSurfaceVariant,
        maxLines = 2,
        overflow = TextOverflow.Ellipsis,
    )
}

@Composable
private fun SwitchTab(vm: RemoteViewModel) {
    val enabled = vm.demoSwitchBlockReason == null
    val current = vm.selectedQuest?.lastDemoId
    ButtonGrid(DemoCatalog.apps, columns = 2) { app, modifier ->
        ActionButton(app.label, enabled = enabled && app.demoId != current, modifier = modifier) { vm.sendSwitch(app.demoId) }
    }
}

@Composable
private fun ControlTab(vm: RemoteViewModel) {
    val enabled = vm.demoSwitchBlockReason == null
    val isVolley = vm.selectedQuest?.lastDemoId == DemoCatalog.VOLLEY_ID
    Text(
        "送信前に前面アプリを確認し、そのアプリへ送ります",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
    ButtonGrid(ControlCatalog.actions, columns = 2) { control, modifier ->
        val allowed = enabled && (control.action != "scene" || isVolley)
        ActionButton(control.label, enabled = allowed, modifier = modifier, outlined = true) { vm.sendControl(control) }
    }
}

@Composable
private fun LaunchTab(vm: RemoteViewModel) {
    val quest = vm.selectedQuest
    val connected = quest?.adb == AdbState.CONNECTED
    Text(
        if (connected) "adb で直接起動します（未インストールのアプリは無効）" else "adb 接続後に使えます",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
    ButtonGrid(DemoCatalog.apps, columns = 2) { app, modifier ->
        val installed = quest?.installed?.contains(app.packageName) == true
        ActionButton(app.label, enabled = connected && installed, modifier = modifier, outlined = true) { vm.launchApp(app) }
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
private fun MirrorHeader(vm: RemoteViewModel, collapsed: Boolean, onToggleCollapsed: (() -> Unit)?) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text("画面ミラー", style = MaterialTheme.typography.titleSmall, modifier = Modifier.weight(1f))
        if (vm.mirrorStatus.startsWith("ミラーエラー")) TextButton(onClick = vm::retryMirror) { Text("再試行") }
        Switch(checked = vm.mirrorEnabled, onCheckedChange = vm::changeMirrorEnabled)
        if (onToggleCollapsed != null) {
            TextButton(onClick = onToggleCollapsed, modifier = Modifier.width(88.dp)) { Text(if (collapsed) "開く" else "たたむ") }
        }
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
        val status = if (vm.mirrorEnabled) vm.mirrorStatus else "ミラーは OFF です"
        if (status.isNotEmpty()) {
            Text(status, color = Color.White, style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(12.dp))
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
