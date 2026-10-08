package com.hapbeat.demoremote.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import com.hapbeat.demoremote.BuildConfig
import com.hapbeat.demoremote.QuestState
import com.hapbeat.demoremote.RemoteViewModel
import com.hapbeat.demoremote.data.MirrorSettings

@Composable
fun SettingsScreen(vm: RemoteViewModel, onBack: () -> Unit, onOpenLicenses: () -> Unit) {
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(
            Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                TextButton(onClick = onBack) { Text("← 戻る") }
                Text("設定", style = MaterialTheme.typography.titleLarge)
            }
            AuthSection(vm)
            HorizontalDivider()
            ControllerSection(vm)
            HorizontalDivider()
            QuestListSection(vm)
            HorizontalDivider()
            UsbAdbSection(vm)
            HorizontalDivider()
            LogSection(vm)
            HorizontalDivider()
            MirrorSection(vm)
            HorizontalDivider()
            LabeledSwitch("画面を常時 ON にする", vm.keepScreenOn, enabled = true, onChange = vm::setKeepScreenOnSetting)
            HorizontalDivider()
            TextButton(onClick = onOpenLicenses) { Text("ライセンス") }
            Text("バージョン ${BuildConfig.VERSION_NAME}", style = MaterialTheme.typography.bodySmall)
        }
    }
}

@Composable
private fun SectionTitle(text: String) = Text(text, style = MaterialTheme.typography.titleMedium)

@Composable
private fun LabeledSwitch(label: String, checked: Boolean, enabled: Boolean, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Text(label, Modifier.weight(1f))
        Switch(checked = checked, onCheckedChange = onChange, enabled = enabled)
    }
}

@Composable
private fun AuthSection(vm: RemoteViewModel) {
    SectionTitle("認証")
    var secret by remember { mutableStateOf("") }
    val config = vm.authConfig
    Text(if (config.hasSecret) "shared secret: 設定済み" else "shared secret: 未設定")
    OutlinedTextField(
        value = secret,
        onValueChange = { secret = it },
        label = { Text("shared secret を入力") },
        singleLine = true,
        visualTransformation = PasswordVisualTransformation(),
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
        modifier = Modifier.fillMaxWidth(),
    )
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        ActionButton("保存", enabled = secret.isNotEmpty()) { vm.setSecret(secret); secret = "" }
        ActionButton("消去", enabled = config.hasSecret, outlined = true) { vm.clearSecret() }
    }
    LabeledSwitch(
        "隔離 LAN で署名なしを使う",
        checked = config.allowUnsigned && !config.hasSecret,
        enabled = !config.hasSecret,
        onChange = vm::setAllowUnsigned,
    )
    Text(
        "署名なしは外部から隔離されたデモ専用 LAN でだけ使ってください。secret 設定中は署名ありで送ります。",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
}

@Composable
private fun ControllerSection(vm: RemoteViewModel) {
    SectionTitle("controller_id")
    var confirm by remember { mutableStateOf(false) }
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text(vm.controllerId, Modifier.weight(1f))
        ActionButton("ID を作り直す", enabled = true, outlined = true) { confirm = true }
    }
    if (confirm) {
        AlertDialog(
            onDismissRequest = { confirm = false },
            title = { Text("ID を作り直しますか？") },
            text = { Text("新しい controller_id を作り、sequence を 1 に戻します。") },
            confirmButton = { TextButton(onClick = { vm.regenerateControllerId(); confirm = false }) { Text("作り直す") } },
            dismissButton = { TextButton(onClick = { confirm = false }) { Text("キャンセル") } },
        )
    }
}

@Composable
private fun QuestListSection(vm: RemoteViewModel) {
    SectionTitle("Quest 一覧")
    vm.quests.forEach { q -> QuestRow(vm, q) }
    LabeledSwitch("PC のエディタも表示", vm.showEditors, enabled = true, onChange = vm::setShowEditorsSetting)
    Text(
        "Unity / Unreal のエディタで動いているデモも Demo Switch に応答します。既定では HMD の一覧に出しません。",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
    var ip by remember { mutableStateOf("") }
    var label by remember { mutableStateOf("") }
    var error by remember { mutableStateOf(false) }
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
        OutlinedTextField(
            value = ip, onValueChange = { ip = it; error = false }, label = { Text("IPv4") }, singleLine = true,
            isError = error, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri), modifier = Modifier.weight(1f),
        )
        OutlinedTextField(value = label, onValueChange = { label = it }, label = { Text("ラベル") }, singleLine = true, modifier = Modifier.weight(1f))
        ActionButton("追加", enabled = ip.isNotBlank()) {
            if (vm.addQuest(ip, label)) { ip = ""; label = "" } else error = true
        }
    }
}

@Composable
private fun QuestRow(vm: RemoteViewModel, quest: QuestState) {
    var label by remember(quest.ip) { mutableStateOf(quest.label) }
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
        OutlinedTextField(
            value = label, onValueChange = { label = it }, label = { Text(quest.ip) }, singleLine = true, modifier = Modifier.weight(1f),
        )
        ActionButton("保存", enabled = label.isNotBlank() && label != quest.label, outlined = true) { vm.renameQuest(quest.ip, label) }
        ActionButton("削除", enabled = true, outlined = true) { vm.removeQuest(quest.ip) }
    }
}

@Composable
private fun UsbAdbSection(vm: RemoteViewModel) {
    SectionTitle("Quest と USB でつなぐ")
    Text(
        "Quest を再起動すると、スマホからの Wi-Fi 接続（adb）が切れます。USB ケーブルで一度つなぐと元に戻ります（PC 不要）。",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
    ActionButton("USB でつなぐ", enabled = true, outlined = true) { vm.openUsbDialog() }
}

@Composable
private fun LogSection(vm: RemoteViewModel) {
    SectionTitle("ログ")
    Text(
        "結果ログと接続の各段階をスマホ内に保存しています（最新 500 行）。不具合の報告に使えます。",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        ActionButton("ログをコピー", enabled = true, outlined = true) { vm.copyLog() }
        ActionButton("ログを消去", enabled = true, outlined = true) { vm.clearLog() }
    }
    // Fixed one line for the copy / clear result.
    Text(vm.logToolStatus.ifEmpty { " " }, style = MaterialTheme.typography.bodySmall, maxLines = 1)
}

@Composable
private fun MirrorSection(vm: RemoteViewModel) {
    SectionTitle("ミラー画質")
    val s = vm.mirrorSettings
    ChoiceRow("最大サイズ", MirrorSettings.MAX_SIZES, s.maxSize, { "$it" }) { vm.updateMirrorSettings(s.copy(maxSize = it)) }
    ChoiceRow("ビットレート", MirrorSettings.BIT_RATES, s.bitRate, { "${it / 1_000_000} Mbps" }) { vm.updateMirrorSettings(s.copy(bitRate = it)) }
    ChoiceRow("フレームレート", MirrorSettings.FPS, s.maxFps, { "$it fps" }) { vm.updateMirrorSettings(s.copy(maxFps = it)) }
    LabeledSwitch("両目を表示", s.bothEyes, enabled = true) { vm.updateMirrorSettings(s.copy(bothEyes = it)) }
    Text(
        "既定は 1024 / 8 Mbps / 30 fps（廉価タブレットでも詰まりにくい値）。",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
}

@Composable
private fun <T> ChoiceRow(label: String, options: List<T>, selected: T, text: (T) -> String, onSelect: (T) -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(label, Modifier.width(110.dp))
        options.forEach { option ->
            FilterChip(selected = option == selected, onClick = { onSelect(option) }, label = { Text(text(option)) })
        }
    }
}
