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
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.hapbeat.demoremote.AdbState
import com.hapbeat.demoremote.LogState
import com.hapbeat.demoremote.RemoteViewModel
import com.hapbeat.demoremote.adb.DeviceAddress
import com.hapbeat.demoremote.adb.DeviceAddressFile
import com.hapbeat.demoremote.data.KeyValueStore
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.text.SimpleDateFormat
import java.time.LocalDateTime
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.util.Date
import java.util.Locale
import javax.swing.JFileChooser

/** One demo of the default set in `tools/install-demos.ps1`. */
data class QuestApk(val name: String, val packageName: String, val fileName: String)

object QuestApkCatalog {
    val apks = listOf(
        QuestApk("Demo Hub", "jp.hapbeat.demohub", "DemoHub.apk"),
        QuestApk("Volley", "jp.hapbeat.volley", "Volley.apk"),
        QuestApk("Boxing", "com.hapbeat.boxing", "Boxing.apk"),
        QuestApk("Hand Demo", "com.Hapbeat.HapticHandDemo_G2", "HandDemo.apk"),
        QuestApk("Safety Mill", "com.hapbeat.safetymill", "SafetyMillVR.apk"),
        QuestApk("T-Rex Encounter", "com.hapbeat.trexencounter", "TRexEncounter.apk"),
        QuestApk("Energy Duel", "jp.hapbeat.energyduel", "EnergyDuel.apk"),
        QuestApk("FPS", "com.hapbeat.fpsdemo", "FPS.apk"),
    )

    /** Shell command: the Quest's UTC offset on one line, then the package's lastUpdateTime (nothing when not installed). */
    fun lastUpdateCommand(packageName: String) = "date +%z; dumpsys package $packageName | grep -m1 lastUpdateTime="

    private val TIME = DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm:ss")

    /** The output of [lastUpdateCommand] as epoch ms; null when the package is not installed or it cannot be read. */
    fun parseLastUpdate(output: String): Long? {
        val offset = Regex("""^\s*([+-]\d{4})\s*$""", RegexOption.MULTILINE).find(output)?.groupValues?.get(1) ?: return null
        val time = Regex("""lastUpdateTime=(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})""").find(output)?.groupValues?.get(1) ?: return null
        return runCatching {
            val zone = ZoneOffset.of(offset.substring(0, 3) + ":" + offset.substring(3))
            OffsetDateTime.of(LocalDateTime.parse(time, TIME), zone).toInstant().toEpochMilli()
        }.getOrNull()
    }

    /** Offered by 「新しい APK を全部インストール」: an APK on the PC that is not on the Quest, or newer than what is. */
    fun isNewer(pcMs: Long?, questMs: Long?): Boolean = pcMs != null && (questMs == null || pcMs > questMs)

    /** Install finished and the Quest's lastUpdateTime says so (it moved, as `-r` always does). */
    fun installVerified(before: Long?, after: Long?): Boolean = after != null && (before == null || after > before)
}

/** State and work of the install screen, kept while the screen is closed so that a running install is not lost. */
class InstallModel(private val vm: RemoteViewModel, private val store: KeyValueStore, private val scope: CoroutineScope, defaultDir: String) {
    data class Row(val apk: QuestApk, val pcMs: Long? = null, val questMs: Long? = null, val status: String = "", val error: Boolean = false)

    var folder by mutableStateOf(store.getString(KEY_DIR) ?: defaultDir)
        private set
    val rows = mutableStateMapOf<String, Row>()
    var running by mutableStateOf(false)
        private set
    var reading by mutableStateOf(false)
        private set
    /** Fixed line under the buttons. */
    var summary by mutableStateOf("")
        private set

    val folderExists: Boolean get() = File(folder).isDirectory

    /** Why installing is off now, or null. */
    val blockReason: String?
        get() = when {
            vm.selectedQuest == null -> "Quest を一覧から選択してください"
            vm.selectedQuest?.adb != AdbState.CONNECTED -> "adb 接続後に使えます"
            !folderExists -> "APK のフォルダを選択してください"
            else -> null
        }

    fun chooseFolder(path: String) {
        folder = path
        store.write { putString(KEY_DIR, path) }
        refresh()
    }

    /** Dates of the APKs on the PC, and of the installed apps when adb is connected. */
    fun refresh() {
        if (reading) return
        reading = true
        val ip = vm.selectedQuest?.takeIf { it.adb == AdbState.CONNECTED }?.ip
        scope.launch {
            try {
                QuestApkCatalog.apks.forEach { apk ->
                    val file = File(folder, apk.fileName)
                    val pcMs = withContext(Dispatchers.IO) { file.takeIf { it.isFile }?.lastModified() }
                    val questMs = ip?.let { readQuestTime(it, apk) }
                    rows[apk.packageName] = (rows[apk.packageName] ?: Row(apk)).copy(pcMs = pcMs, questMs = questMs)
                }
            } finally {
                reading = false
            }
        }
    }

    fun install(apk: QuestApk) {
        if (running || blockReason != null) return
        running = true
        scope.launch {
            try {
                installOne(apk)
            } finally {
                running = false
                vm.refreshInstalledPackages()
            }
        }
    }

    fun installAllNewer() {
        if (running || blockReason != null) return
        val targets = QuestApkCatalog.apks.filter { apk -> rows[apk.packageName]?.let { QuestApkCatalog.isNewer(it.pcMs, it.questMs) } == true }
        if (targets.isEmpty()) {
            summary = "新しい APK はありません"
            return
        }
        running = true
        scope.launch {
            try {
                val results = targets.map { installOne(it) }
                val failed = results.count { !it }
                summary = "${results.size} 件中 ${results.size - failed} 件インストール済み" + if (failed > 0) "・失敗 $failed 件" else ""
            } finally {
                running = false
                vm.refreshInstalledPackages()
            }
        }
    }

    /** One APK with replace (keeps the app's data) over a connection of its own, verified by the Quest's lastUpdateTime. */
    private suspend fun installOne(apk: QuestApk): Boolean {
        val ip = vm.selectedIp ?: return false
        val file = File(folder, apk.fileName)
        fun update(block: (Row) -> Row) { rows[apk.packageName] = block(rows[apk.packageName] ?: Row(apk)) }
        if (!file.isFile) {
            update { it.copy(status = "APK がありません", error = true) }
            return false
        }
        val before = readQuestTime(ip, apk)
        val megabytes = file.length() / 1_048_576
        val started = System.nanoTime()
        val ticker = scope.launch {
            while (true) {
                val seconds = (System.nanoTime() - started) / 1_000_000_000
                update { it.copy(status = "転送・インストール中（$megabytes MB・$seconds 秒）", error = false) }
                delay(1_000)
            }
        }
        val error = withContext(Dispatchers.IO) {
            runCatching { vm.openAdbConnection(ip).use { it.install(file, "-r") } }.exceptionOrNull()
        }
        ticker.cancel()
        if (error != null) {
            update { it.copy(status = "失敗: ${error.message ?: error.javaClass.simpleName}", error = true) }
            return false
        }
        val after = readQuestTime(ip, apk)
        val verified = QuestApkCatalog.installVerified(before, after)
        update { it.copy(questMs = after, status = if (verified) "インストール済み（確認済み）" else "更新日時を確認できません", error = !verified) }
        return verified
    }

    private suspend fun readQuestTime(ip: String, apk: QuestApk): Long? =
        vm.questShell(ip, QuestApkCatalog.lastUpdateCommand(apk.packageName))?.let { QuestApkCatalog.parseLastUpdate(it.output) }

    private companion object {
        const val KEY_DIR = "desktop_apk_dir"
    }
}

private val DateText = SimpleDateFormat("yyyy-MM-dd HH:mm", Locale.US)

private fun dateText(ms: Long?): String = if (ms == null) "-" else DateText.format(Date(ms))

/** Installs the demo APKs on the selected Quest and sets its Hapbeat group, with no typed adb command. */
@Composable
fun InstallScreen(vm: RemoteViewModel, model: InstallModel, onBack: () -> Unit) {
    LaunchedEffect(vm.selectedIp, vm.selectedQuest?.adb) { model.refresh() }
    // No folder at the place known at build time (another PC, another checkout): ask once when the screen opens.
    LaunchedEffect(Unit) { if (!model.folderExists) pickFolder(model) }
    val block = model.blockReason
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                TextButton(onClick = onBack) { Text("← 戻る") }
                Text("Quest へのインストール", style = MaterialTheme.typography.titleLarge)
            }
            Text("宛先: ${vm.selectedQuest?.let { "${it.label}  ${it.ip}" } ?: "-"}", fontWeight = FontWeight.Bold)
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("APK のフォルダ", Modifier.width(112.dp))
                Text(model.folder, Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis,
                    color = if (model.folderExists) MaterialTheme.colorScheme.onSurface else StatusRed)
                ActionButton("フォルダを選択", enabled = !model.running, outlined = true) { pickFolder(model) }
            }
            // Fixed line: why it is off, else the last result.
            Text(block ?: model.summary.ifEmpty { " " }, style = MaterialTheme.typography.bodySmall,
                color = if (block != null) StatusRed else MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                ActionButton("新しい APK を全部インストール", enabled = block == null && !model.running) { model.installAllNewer() }
                TextButton(onClick = model::refresh, enabled = !model.reading, modifier = Modifier.width(96.dp)) {
                    Text(if (model.reading) "読込中" else "再読込")
                }
            }
            HeaderRow()
            QuestApkCatalog.apks.forEach { apk ->
                val row = model.rows[apk.packageName] ?: InstallModel.Row(apk)
                Row(
                    Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surfaceVariant).padding(start = 8.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Text(apk.name, Modifier.width(NAME_WIDTH), fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(dateText(row.pcMs), Modifier.width(DATE_WIDTH), style = MaterialTheme.typography.bodySmall, maxLines = 1)
                    Text(dateText(row.questMs), Modifier.width(DATE_WIDTH), style = MaterialTheme.typography.bodySmall, maxLines = 1)
                    Text(
                        row.status.ifEmpty { if (QuestApkCatalog.isNewer(row.pcMs, row.questMs)) "新しい APK あり" else " " },
                        Modifier.weight(1f),
                        style = MaterialTheme.typography.bodySmall,
                        color = if (row.error) StatusRed else MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis,
                    )
                    ActionButton(
                        "インストール",
                        enabled = block == null && !model.running && row.pcMs != null,
                        modifier = Modifier.width(128.dp),
                        outlined = true,
                    ) { model.install(apk) }
                }
            }
            HorizontalDivider(Modifier.padding(vertical = 8.dp))
            GroupSection(vm)
        }
    }
}

@Composable
private fun HeaderRow() {
    Row(Modifier.fillMaxWidth().padding(start = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        val style = MaterialTheme.typography.labelMedium
        Text("デモ", Modifier.width(NAME_WIDTH), style = style)
        Text("PC の APK", Modifier.width(DATE_WIDTH), style = style)
        Text("Quest 内", Modifier.width(DATE_WIDTH), style = style)
        Text("状態", Modifier.weight(1f), style = style)
    }
}

/** Writes the group into every demo on this Quest, as `install-demos.ps1 -Group n` does (player not set). */
@Composable
private fun GroupSection(vm: RemoteViewModel) {
    Text("この Quest の Hapbeat グループ", style = MaterialTheme.typography.titleSmall)
    val quest = vm.selectedQuest
    val block = vm.deviceAddressBlockReason
    var group by remember(quest?.ip) { mutableStateOf(1) }
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Text("現在: ${DeviceAddressFile.groupStatus(quest?.address)}", Modifier.width(200.dp), maxLines = 1)
        TextButton(onClick = { group = DeviceAddressFile.step(group, -1) }, enabled = group != DeviceAddressFile.UNSET) { Text("−") }
        // Fixed width so "指定なし" / "99" do not move the buttons.
        Text(DeviceAddressFile.axisText(group), Modifier.width(80.dp), textAlign = TextAlign.Center)
        TextButton(onClick = { group = DeviceAddressFile.step(group, 1) }, enabled = group != DeviceAddressFile.VALUES.last) { Text("＋") }
        ActionButton(
            if (vm.deviceAddressWriting) "書込中" else "全デモに書き込み",
            enabled = block == null && !vm.deviceAddressWriting,
            modifier = Modifier.width(176.dp),
        ) { vm.writeDeviceAddress(DeviceAddress(DeviceAddressFile.UNSET, group)) }
    }
    val status = vm.deviceAddressStatus
    Text(
        block ?: status?.first ?: " ",
        style = MaterialTheme.typography.bodySmall,
        color = when {
            block != null -> StatusRed
            status?.second == LogState.READY -> StatusGreen
            status?.second == LogState.FAILED || status?.second == LogState.ERROR -> StatusRed
            else -> MaterialTheme.colorScheme.onSurfaceVariant
        },
        maxLines = 1,
        overflow = TextOverflow.Ellipsis,
    )
}

private fun pickFolder(model: InstallModel) {
    val chooser = JFileChooser(File(model.folder).takeIf { it.isDirectory }).apply {
        fileSelectionMode = JFileChooser.DIRECTORIES_ONLY
        dialogTitle = "APK のフォルダを選択"
    }
    if (chooser.showOpenDialog(null) == JFileChooser.APPROVE_OPTION) model.chooseFolder(chooser.selectedFile.path)
}

private val NAME_WIDTH = 150.dp
private val DATE_WIDTH = 120.dp
