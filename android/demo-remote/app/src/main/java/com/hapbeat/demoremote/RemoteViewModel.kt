package com.hapbeat.demoremote

import android.app.Application
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.hardware.usb.UsbDevice
import android.hardware.usb.UsbManager
import android.os.Build
import android.view.Surface
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.core.content.ContextCompat
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.hapbeat.demoremote.adb.AdbConnectResult
import com.hapbeat.demoremote.adb.AdbKeys
import com.hapbeat.demoremote.adb.QuestAdb
import com.hapbeat.demoremote.adb.UsbAdb
import com.hapbeat.demoremote.adb.UsbAdbException
import com.hapbeat.demoremote.adb.UsbAdbKey
import com.hapbeat.demoremote.data.MirrorSettings
import com.hapbeat.demoremote.data.RemotePreset
import com.hapbeat.demoremote.data.SavedQuest
import com.hapbeat.demoremote.data.SettingsStore
import com.hapbeat.demoremote.mirror.MirrorSession
import com.hapbeat.demoremote.net.AdbPortScanner
import com.hapbeat.demoremote.net.DemoSwitchSocket
import com.hapbeat.demoremote.net.WifiBinding
import com.hapbeat.demoremote.protocol.AuthConfig
import com.hapbeat.demoremote.protocol.DemoSwitchMessage
import com.hapbeat.demoremote.protocol.DemoSwitchProtocol
import com.hapbeat.demoremote.protocol.SequenceReservation
import com.hapbeat.demoremote.protocol.SequenceReserver
import dadb.AdbKeyPair
import dadb.Dadb
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

enum class AdbState { DISCONNECTED, CONNECTING, AUTH_WAIT, CONNECTED }

data class QuestState(
    val ip: String,
    val label: String,
    val model: String = "",
    val lastDemoId: String = "",
    val lastSeenAtMs: Long = 0,
    /** null = not probed yet in this foreground session. */
    val respondedLastRound: Boolean? = null,
    val adb: AdbState = AdbState.DISCONNECTED,
    /** TCP 5555 answered in the last subnet scan (Wi-Fi adb on); null = not scanned yet. */
    val adbPortOpen: Boolean? = null,
    /** ro.serialno; lets a saved Quest follow its new IP after a DHCP change. */
    val serial: String = "",
    /** Haptics state from the last READY haptics_on/off in the current foreground demo; null = unknown. */
    val hapticsOn: Boolean? = null,
    /** Last STATE reply (QUERY); null when the receiver does not support QUERY or the demo changed. */
    val remoteState: DemoSwitchMessage.State? = null,
    /** Added by hand; kept (and saved) even before adb has identified it. */
    val manual: Boolean = false,
    val battery: Int? = null,
    /** Installed packages from `pm list packages`; null until adb has connected. */
    val installed: Set<String>? = null,
)

enum class LogState { SENT, ACK, READY, FAILED, NO_RESPONSE, INFO, ERROR }

data class LogEntry(
    val id: Long,
    val time: String,
    val target: String,
    val content: String,
    val seq: Long?,
    val state: LogState,
    val detail: String = "",
)

private class PendingCommand(
    val seq: Long, val demoId: String, val ip: String, val logId: Long, val action: String, var terminal: Boolean = false,
)

class RemoteViewModel(app: Application) : AndroidViewModel(app) {
    private val settings = SettingsStore(app)
    private val reserver = SequenceReserver(settings)
    private val keyPair: AdbKeyPair by lazy { AdbKeys.load(app) }
    private val socket = DemoSwitchSocket(viewModelScope) { source, payload ->
        viewModelScope.launch(Dispatchers.Main) { onPayload(source, payload) }
    }
    private val wifi = WifiBinding(app) {
        viewModelScope.launch(Dispatchers.Main) { if (foreground) reopenSocket() }
    }

    // ---- observable state ----
    var controllerId by mutableStateOf(settings.controllerId); private set
    var authConfig by mutableStateOf(settings.authConfig); private set
    var authChosen by mutableStateOf(settings.authChosen); private set
    val quests = mutableStateListOf<QuestState>().apply {
        // Only headsets identified by adb (serial) or added by hand are remembered; a Demo Switch responder seen
        // once (e.g. a PC running a demo in the editor) is not. Older "Quest N" labels read like model names.
        addAll(settings.loadQuests().filter { it.serial.isNotEmpty() || it.manual }.map {
            QuestState(it.ip, it.label.replace(Regex("^Quest (\\d+)$"), "HMD #$1"), it.model, "", it.lastSeenAtMs,
                serial = it.serial, manual = it.manual)
        })
    }
    var selectedIp by mutableStateOf(settings.selectedIp); private set
    val logs = mutableStateListOf<LogEntry>()
    /** Session plans kept on this phone (the Hub's presets 1..3 are separate and edited in VR). */
    val presets = mutableStateListOf<RemotePreset>().apply { addAll(settings.presets) }
    /** Fixed-area notice (text, isError). */
    var notice by mutableStateOf("" to false); private set
    var adbMessage by mutableStateOf(""); private set
    var mirrorSettings by mutableStateOf(settings.mirrorSettings); private set
    var mirrorEnabled by mutableStateOf(settings.mirrorEnabled); private set
    var mirrorVideoSize by mutableStateOf<Pair<Int, Int>?>(null); private set
    var mirrorStatus by mutableStateOf(""); private set
    var keepScreenOn by mutableStateOf(settings.keepScreenOn); private set
    var discovering by mutableStateOf(false); private set
    var scanningAdb by mutableStateOf(false); private set
    /** The last Wi-Fi adb connect to the selected Quest failed because port 5555 is closed (USB re-enable helps). */
    var wifiAdbOff by mutableStateOf(false); private set
    var usbDialogOpen by mutableStateOf(false); private set
    var usbAdbRunning by mutableStateOf(false); private set
    /** USB re-enable progress / result (text, isError), shown in a fixed area of the dialog. */
    var usbAdbStatus by mutableStateOf("" to false); private set

    val selectedQuest: QuestState? get() = quests.firstOrNull { it.ip == selectedIp }

    // ---- internal state (main thread only) ----
    private var foreground = false
    private var discoveryLoop: Job? = null
    private val activeNonces = mutableMapOf<String, (String, DemoSwitchMessage.Here) -> Unit>()
    /** QUERY nonce -> Quest IP it was sent to. */
    private val queryNonces = mutableMapOf<String, String>()
    private val pending = mutableMapOf<Long, PendingCommand>()
    private var nextLogId = 1L
    private val adbConnections = mutableMapOf<String, QuestAdb>()
    private var adbJob: Job? = null
    private var batteryJob: Job? = null
    private var mirrorSurface: Surface? = null
    private var mirrorSession: MirrorSession? = null
    private val timeFormat = SimpleDateFormat("HH:mm:ss", Locale.US)

    // ---- lifecycle ----------------------------------------------------------------------

    init {
        wifi.start()
    }

    private fun reopenSocket() {
        socket.close()
        try {
            socket.open()
        } catch (e: java.net.SocketException) {
            setNotice("UDP ソケットを開けません: ${e.message}", true)
        }
    }

    fun onForeground() {
        foreground = true
        reopenSocket()
        quests.indices.forEach { quests[it] = quests[it].copy(respondedLastRound = null) }
        discoveryLoop?.cancel()
        discoveryLoop = viewModelScope.launch {
            while (isActive) {
                runDiscoveryRound()
                selectedQuest?.takeIf { it.respondedLastRound == true }?.let { queryState(it.ip) }
                delay(DISCOVERY_INTERVAL_MS)
            }
        }
        startBatteryPolling()
        scanAdbHosts()
        if (selectedQuest?.adb == AdbState.DISCONNECTED) connectAdb()
        updateMirror()
    }

    fun onBackground() {
        foreground = false
        discoveryLoop?.cancel()
        discoveryLoop = null
        batteryJob?.cancel()
        socket.close()
        activeNonces.clear()
        stopMirror()
    }

    override fun onCleared() {
        stopMirror()
        socket.close()
        adbConnections.values.forEach { it.close() }
        wifi.stop()
    }

    // ---- authentication / controller ----------------------------------------------------

    fun setSecret(secret: String) {
        if (secret.isEmpty()) return
        settings.setSecret(secret)
        refreshAuth()
    }

    fun clearSecret() {
        settings.clearSecret()
        refreshAuth()
    }

    fun setAllowUnsigned(allow: Boolean) {
        if (authConfig.hasSecret) return
        settings.setAllowUnsigned(allow)
        refreshAuth()
    }

    private fun refreshAuth() {
        authConfig = settings.authConfig
        authChosen = settings.authChosen
    }

    fun regenerateControllerId() {
        if (!settings.regenerateControllerId()) {
            setNotice("controller_id を保存できませんでした", true)
            return
        }
        controllerId = settings.controllerId
        pending.clear()
    }

    /** Reason Demo Switch buttons are disabled, or null when sending is possible. */
    val demoSwitchBlockReason: String?
        get() = when {
            !authConfig.canSend -> "認証が未設定です（設定で shared secret か「隔離 LAN で署名なし」を選択）"
            selectedQuest == null -> "Quest を一覧から選択してください"
            else -> null
        }

    // ---- Quest list ----------------------------------------------------------------------

    fun selectQuest(ip: String) {
        if (selectedIp == ip) return
        stopMirror()
        selectedIp = ip
        settings.selectedIp = ip
        mirrorVideoSize = null
        adbMessage = ""
        wifiAdbOff = false
        if (selectedQuest?.adb == AdbState.DISCONNECTED && foreground) connectAdb()
        updateMirror()
    }

    fun addQuest(ip: String, label: String): Boolean {
        val trimmed = ip.trim()
        if (!DemoSwitchProtocol.isUnicastIpv4(trimmed)) return false
        if (quests.any { it.ip == trimmed }) return true
        quests.add(QuestState(trimmed, label.ifBlank { nextLabel() }, manual = true))
        persistQuests()
        return true
    }

    fun renameQuest(ip: String, label: String) {
        updateQuest(ip) { it.copy(label = label) }
        persistQuests()
    }

    fun removeQuest(ip: String) {
        if (selectedIp == ip) {
            stopMirror()
            selectedIp = null
            settings.selectedIp = null
        }
        adbConnections.remove(ip)?.close()
        quests.removeAll { it.ip == ip }
        persistQuests()
    }

    private fun updateQuest(ip: String, block: (QuestState) -> QuestState) {
        val i = quests.indexOfFirst { it.ip == ip }
        if (i >= 0) quests[i] = block(quests[i])
    }

    private fun unconfirmedLabel(ip: String) = "未確認 ." + ip.substringAfterLast('.')

    /** "HMD #n" with the smallest unused n ("Quest n" would read like a model name). */
    private fun nextLabel(): String {
        val used = quests.mapNotNull { Regex("^HMD #(\\d+)$").find(it.label)?.groupValues?.get(1)?.toInt() }.toSet()
        return "HMD #" + generateSequence(1) { it + 1 }.first { it !in used }
    }

    private fun persistQuests() {
        settings.saveQuests(quests.filter { it.serial.isNotEmpty() || it.manual }.map {
            SavedQuest(it.ip, it.label, it.model, it.lastDemoId, it.lastSeenAtMs, it.serial, it.manual)
        })
    }

    /** A new foreground demo starts with its own haptics state, so the remembered one no longer applies. */
    private fun withForegroundDemo(quest: QuestState, demoId: String): QuestState =
        quest.copy(lastDemoId = demoId, lastSeenAtMs = System.currentTimeMillis(),
            hapticsOn = if (demoId == quest.lastDemoId) quest.hapticsOn else null,
            remoteState = quest.remoteState?.takeIf { it.currentDemoId == demoId })

    /**
     * [ip] turned out to be the headset [serial]. If a saved entry with the same serial sits on an
     * older IP, carry its label (and selection) over to [ip] and drop the stale entry.
     */
    private fun adoptSerial(ip: String, serial: String) {
        if (serial.isEmpty()) return
        val stale = quests.firstOrNull { it.serial == serial && it.ip != ip }
        updateQuest(ip) { it.copy(serial = serial, label = stale?.label ?: it.label) }
        if (stale != null) {
            adbConnections.remove(stale.ip)?.close()
            quests.removeAll { it.ip == stale.ip }
            if (selectedIp == stale.ip) {
                stopMirror()
                selectedIp = ip
                settings.selectedIp = ip
                mirrorVideoSize = null
            }
            addLog(stale.label, "IP が変わったため ${stale.ip} → $ip に更新しました", null, LogState.INFO)
        }
        persistQuests()
    }

    // ---- discovery -------------------------------------------------------------------------

    fun rediscover() {
        viewModelScope.launch { runDiscoveryRound() }
        scanAdbHosts()
    }

    /**
     * Lists hosts with Wi-Fi adb open, so a Quest on its home screen (no Demo Switch receiver to
     * answer DISCOVER) can still be selected for "Hub を開く" and the mirror. TCP connect only.
     */
    private fun scanAdbHosts() {
        if (scanningAdb) return
        scanningAdb = true
        viewModelScope.launch {
            try {
                val local = withContext(Dispatchers.IO) { DemoSwitchSocket.localIpv4(getApplication(), wifi.network) } ?: return@launch
                val hosts = AdbPortScanner.subnetHosts(local.address.address, local.prefixLength)
                val ignored = settings.ignoredAdbHosts
                val open = AdbPortScanner.scan(hosts).filter { it !in ignored }.toSet()
                // Port 5555 alone does not prove a Quest (a phone left in `adb tcpip` answers too): such a host is
                // shown as unconfirmed until adb reports its model.
                open.filter { ip -> quests.none { it.ip == ip } }.forEach { ip ->
                    quests.add(QuestState(ip, unconfirmedLabel(ip)))
                }
                quests.indices.forEach { quests[it] = quests[it].copy(adbPortOpen = quests[it].ip in open) }
                if (open.isNotEmpty()) persistQuests()
                followSelectedQuest(open)
            } finally {
                scanningAdb = false
            }
        }
    }

    /** Broadcast + unicast to known Quests, 700 ms window, fresh nonce. */
    private suspend fun runDiscoveryRound() {
        val config = authConfig
        if (!config.canSend || !socket.isOpen || discovering) return
        discovering = true
        val nonce = DemoSwitchProtocol.newNonce()
        val responders = mutableSetOf<String>()
        activeNonces[nonce] = { source, here ->
            responders += source
            onHere(source, here)
        }
        val payload = DemoSwitchProtocol.buildDiscover(controllerId, nonce, config)
        val broadcast = withContext(Dispatchers.IO) { DemoSwitchSocket.broadcastAddress(getApplication(), wifi.network) }
        socket.send(broadcast, payload)
        quests.map { it.ip }.forEach { socket.send(it, payload) }
        delay(DISCOVERY_WINDOW_MS)
        activeNonces.remove(nonce)
        quests.indices.forEach {
            val q = quests[it]
            // No answer: whatever was in front before is no longer known (home screen, asleep, other app).
            quests[it] = if (q.ip in responders) q.copy(respondedLastRound = true)
            else q.copy(respondedLastRound = false, lastDemoId = "", remoteState = null)
        }
        // Forget unidentified responders that went quiet (never adb, not added by hand, not selected).
        quests.removeAll {
            it.serial.isEmpty() && !it.manual && it.respondedLastRound == false && it.adbPortOpen != true &&
                it.adb == AdbState.DISCONNECTED && it.ip != selectedIp
        }
        discovering = false
    }

    private fun onHere(source: String, here: DemoSwitchMessage.Here) {
        if (quests.none { it.ip == source }) {
            quests.add(QuestState(source, nextLabel()))
        }
        updateQuest(source) { withForegroundDemo(it, here.currentDemoId).copy(respondedLastRound = true) }
        persistQuests()
    }

    /**
     * The selected Quest is gone from its saved IP (DHCP gave it a new one): try the other hosts with
     * Wi-Fi adb open and re-select the one with the same serial. A headset that already trusts this
     * phone answers at once; others time out quickly and are left alone.
     */
    private suspend fun followSelectedQuest(open: Set<String>) {
        val target = selectedQuest ?: return
        if (target.serial.isEmpty() || target.ip in open || target.adb != AdbState.DISCONNECTED) return
        val keys = withContext(Dispatchers.IO) { keyPair }
        for (ip in open) {
            val entry = quests.firstOrNull { it.ip == ip }
            if (entry != null && (entry.serial.isNotEmpty() || entry.adb != AdbState.DISCONNECTED)) continue
            val adb = QuestAdb(ip, keys)
            if (adb.connect(onAuthWaiting = {}, authWaitMs = FOLLOW_AUTH_WAIT_MS) != AdbConnectResult.Connected) continue
            val serial = runCatching { adb.shell("getprop ro.serialno").output.trim() }.getOrDefault("")
            if (serial != target.serial) { adb.close(); continue }
            adbConnections.remove(ip)?.close()
            adbConnections[ip] = adb
            updateQuest(ip) { it.copy(adb = AdbState.CONNECTED) }
            adoptSerial(ip, serial)
            adbMessage = ""
            refreshAdbInfo(ip)
            updateMirror()
            return
        }
    }

    // ---- receive -----------------------------------------------------------------------------

    private fun onPayload(source: String, payload: ByteArray) {
        val message = DemoSwitchProtocol.parse(payload) ?: return
        val config = authConfig
        when (message) {
            is DemoSwitchMessage.Here -> {
                if (!DemoSwitchProtocol.acceptHere(message, source, controllerId, activeNonces.keys, config)) return
                activeNonces[message.nonce]?.invoke(source, message)
            }
            is DemoSwitchMessage.State -> {
                if (!DemoSwitchProtocol.acceptState(message, source, controllerId, { queryNonces[it] }, config)) return
                queryNonces.remove(message.nonce)
                updateQuest(source) { withForegroundDemo(it, message.currentDemoId).copy(remoteState = message, hapticsOn = message.hapticsOn) }
            }
            is DemoSwitchMessage.Status -> {
                val ok = DemoSwitchProtocol.acceptStatus(message, source, controllerId, { seq, demoId ->
                    pending[seq]?.takeIf { it.demoId == demoId }?.ip
                }, config)
                if (ok) onStatus(message)
            }
            else -> Unit // controllers ignore commands / discovery requests
        }
    }

    private fun onStatus(status: DemoSwitchMessage.Status) {
        val command = pending[status.seq] ?: return
        if (command.terminal) return
        when (status.type) {
            "ACK" -> updateLog(command.logId) { it.copy(state = LogState.ACK) }
            "READY" -> {
                command.terminal = true
                updateLog(command.logId) { it.copy(state = LogState.READY) }
                queryState(command.ip)
                updateQuest(command.ip) {
                    val q = withForegroundDemo(it, status.currentDemoId)
                    when (command.action) {
                        "haptics_on" -> q.copy(hapticsOn = true)
                        "haptics_off" -> q.copy(hapticsOn = false)
                        else -> q
                    }
                }
                persistQuests()
            }
            "FAILED" -> {
                command.terminal = true
                val detail = if (status.message.isEmpty()) status.code else "${status.code}: ${status.message}"
                updateLog(command.logId) { it.copy(state = LogState.FAILED, detail = detail) }
            }
        }
    }

    /** Asks [ip] for its state (QUERY -> STATE). Receivers without QUERY simply do not answer. */
    private fun queryState(ip: String) {
        val config = authConfig
        if (!config.canSend || !socket.isOpen) return
        val nonce = DemoSwitchProtocol.newNonce()
        queryNonces[nonce] = ip
        viewModelScope.launch {
            socket.send(ip, DemoSwitchProtocol.buildQuery(controllerId, nonce, config))
            delay(QUERY_TIMEOUT_MS)
            queryNonces.remove(nonce)
        }
    }

    // ---- send ----------------------------------------------------------------------------------

    fun sendSwitch(demoId: String) {
        val quest = selectedQuest ?: return
        val config = authConfig
        if (!config.canSend) return
        viewModelScope.launch {
            val seq = reserveSeq() ?: return@launch
            val payload = DemoSwitchProtocol.buildSwitch(controllerId, seq, demoId, config)
            send(quest, seq, demoId, "切替 → ${DemoCatalog.labelFor(demoId)}", payload, SWITCH_TIMEOUT_MS, "")
        }
    }

    /** Confirms the foreground demo with a unicast DISCOVER first (spec), then sends CONTROL to it. */
    fun sendControl(control: ControlAction) {
        val quest = selectedQuest ?: return
        val config = authConfig
        if (!config.canSend) return
        viewModelScope.launch {
            val current = probeForegroundDemo(quest.ip, config)
            if (current == null) {
                addLog(quest.label, control.label, null, LogState.ERROR,
                    "Demo Switch 受信アプリが前面にいません（Hub は『Hub を開く』で起動できます）")
                return@launch
            }
            if (control.demoId != null && current != control.demoId) {
                addLog(quest.label, control.label, null, LogState.ERROR,
                    "前面アプリが ${DemoCatalog.labelFor(control.demoId)} ではありません（${DemoCatalog.labelFor(current)}）")
                return@launch
            }
            val seq = reserveSeq() ?: return@launch
            val payload = DemoSwitchProtocol.buildControl(controllerId, seq, current, control.action, control.sceneId, config)
            send(quest, seq, current, "${control.label}（${DemoCatalog.labelFor(current)}）", payload, CONTROL_TIMEOUT_MS, control.action)
        }
    }

    private suspend fun probeForegroundDemo(ip: String, config: AuthConfig): String? {
        if (!socket.isOpen) return null
        val nonce = DemoSwitchProtocol.newNonce()
        val result = CompletableDeferred<String>()
        activeNonces[nonce] = { source, here ->
            if (source == ip) {
                onHere(source, here)
                result.complete(here.currentDemoId)
            }
        }
        try {
            if (!socket.send(ip, DemoSwitchProtocol.buildDiscover(controllerId, nonce, config))) return null
            return withTimeoutOrNull(DISCOVERY_WINDOW_MS) { result.await() }
        } finally {
            activeNonces.remove(nonce)
        }
    }

    private suspend fun reserveSeq(): Long? {
        return when (val r = withContext(Dispatchers.IO) { reserver.reserve() }) {
            is SequenceReservation.Reserved -> r.seq
            SequenceReservation.Exhausted -> { setNotice("sequence が上限に達しました。設定で ID を作り直してください", true); null }
            SequenceReservation.PersistFailed -> { setNotice("sequence を保存できなかったため送信を中止しました", true); null }
        }
    }

    private suspend fun send(
        quest: QuestState, seq: Long, demoId: String, content: String, payload: String, timeoutMs: Long, action: String,
    ) {
        val logId = addLog(quest.label, content, seq, LogState.SENT)
        val command = PendingCommand(seq, demoId, quest.ip, logId, action)
        pending[seq] = command
        if (!socket.send(quest.ip, payload)) {
            pending.remove(seq)
            updateLog(logId) { it.copy(state = LogState.ERROR, detail = "UDP 送信に失敗しました") }
            return
        }
        viewModelScope.launch {
            delay(timeoutMs)
            if (!command.terminal) {
                command.terminal = true
                updateLog(logId) { it.copy(state = LogState.NO_RESPONSE, detail = "応答なし") }
            }
            pending.remove(seq)
        }
    }

    // ---- log -----------------------------------------------------------------------------------

    private fun addLog(target: String, content: String, seq: Long?, state: LogState, detail: String = ""): Long {
        val id = nextLogId++
        logs.add(0, LogEntry(id, timeFormat.format(Date()), target, content, seq, state, detail))
        while (logs.size > MAX_LOGS) logs.removeAt(logs.lastIndex)
        return id
    }

    private fun updateLog(id: Long, block: (LogEntry) -> LogEntry) {
        val i = logs.indexOfFirst { it.id == id }
        if (i >= 0) logs[i] = block(logs[i])
    }

    private fun setNotice(text: String, error: Boolean) {
        notice = text to error
    }

    // ---- adb -------------------------------------------------------------------------------------

    fun connectAdb() {
        val ip = selectedIp ?: return
        adbJob?.cancel()
        adbJob = viewModelScope.launch {
            val keys = withContext(Dispatchers.IO) { keyPair } // first use generates RSA-2048
            val adb = adbConnections.getOrPut(ip) { QuestAdb(ip, keys) }
            updateQuest(ip) { it.copy(adb = AdbState.CONNECTING) }
            // Elapsed seconds so a slow handshake reads as progress, not a hang.
            val startedAt = System.currentTimeMillis()
            val ticker = launch {
                while (isActive) {
                    val s = (System.currentTimeMillis() - startedAt) / 1000
                    if (quests.firstOrNull { it.ip == ip }?.adb == AdbState.CONNECTING) adbMessage = "adb 接続中…（${s} 秒）"
                    delay(1_000)
                }
            }
            val result = try {
                adb.connect(onAuthWaiting = {
                    viewModelScope.launch(Dispatchers.Main) {
                        updateQuest(ip) { it.copy(adb = AdbState.AUTH_WAIT) }
                        adbMessage = "ヘッドセット内で「このコンピューターから常に許可」にチェックして許可してください（最大 30 秒）"
                    }
                })
            } catch (e: kotlinx.coroutines.CancellationException) {
                updateQuest(ip) { it.copy(adb = AdbState.DISCONNECTED) }
                adbMessage = "adb 接続を中止しました"
                throw e
            } finally {
                ticker.cancel()
            }
            val label = quests.firstOrNull { it.ip == ip }?.label ?: ip
            if (ip == selectedIp) wifiAdbOff = result == AdbConnectResult.Unreachable
            if (result != AdbConnectResult.Connected) addLog(label, "adb 接続 $ip", null, LogState.ERROR, result.toString())
            when (result) {
                AdbConnectResult.Connected -> {
                    updateQuest(ip) { it.copy(adb = AdbState.CONNECTED) }
                    adbMessage = ""
                    refreshAdbInfo(ip)
                    updateMirror()
                }
                AdbConnectResult.Unreachable -> adbFailed(ip,
                    "Quest の Wi-Fi adb が無効です（再起動後など）。右の「USB で有効化」でスマホと直結して有効にしてください")
                AdbConnectResult.AuthTimeout -> adbFailed(ip, "adb の許可が得られませんでした。ヘッドセット内の許可ダイアログを確認して再接続してください")
                AdbConnectResult.Rejected -> adbFailed(ip, "adb の接続が拒否されました。ヘッドセット内で許可してから再接続してください")
                is AdbConnectResult.Failed -> adbFailed(ip, "adb 接続に失敗しました（${result.reason}）")
            }
        }
    }

    fun cancelAdbConnect() {
        adbJob?.cancel()
    }

    private fun adbFailed(ip: String, message: String) {
        updateQuest(ip) { it.copy(adb = AdbState.DISCONNECTED) }
        if (ip == selectedIp) adbMessage = message
    }

    private fun adbLost(ip: String) {
        adbConnections[ip]?.close()
        updateQuest(ip) { it.copy(adb = AdbState.DISCONNECTED, battery = null) }
        if (ip == selectedIp) {
            stopMirror()
            adbMessage = "adb 接続が切れました。再接続しています…"
            // One automatic retry (Quest woke up / Wi-Fi came back); a failure shows its own guidance.
            if (foreground) connectAdb()
        }
    }

    /** Runs [command] on [ip]; I/O failure marks the connection lost. */
    private suspend fun adbShell(ip: String, command: String): dadb.AdbShellResponse? {
        val adb = adbConnections[ip] ?: return null
        return try {
            adb.shell(command)
        } catch (e: kotlinx.coroutines.CancellationException) {
            throw e
        } catch (_: Exception) {
            adbLost(ip)
            null
        }
    }

    private suspend fun refreshAdbInfo(ip: String) {
        val model = adbShell(ip, "getprop ro.product.model")?.output?.let { QuestAdb.parseModel(it) } ?: return
        if (!model.startsWith("Quest")) {
            // Not a headset: drop it and skip it in later scans.
            addLog(ip, "$model は Quest ではないため一覧から外しました", null, LogState.INFO)
            settings.ignoredAdbHosts = settings.ignoredAdbHosts + ip
            removeQuest(ip)
            return
        }
        if (quests.firstOrNull { it.ip == ip }?.label == unconfirmedLabel(ip)) updateQuest(ip) { it.copy(label = nextLabel()) }
        val serial = adbShell(ip, "getprop ro.serialno")?.output?.trim() ?: return
        adoptSerial(ip, serial)
        val packages = adbShell(ip, "pm list packages")?.output?.let { QuestAdb.parsePackages(it) } ?: return
        val battery = adbShell(ip, "dumpsys battery")?.output?.let { QuestAdb.parseBatteryLevel(it) }
        updateQuest(ip) { it.copy(model = model, installed = packages, battery = battery) }
        persistQuests()
    }

    private fun startBatteryPolling() {
        batteryJob?.cancel()
        batteryJob = viewModelScope.launch {
            while (isActive) {
                delay(BATTERY_INTERVAL_MS)
                quests.filter { it.adb == AdbState.CONNECTED }.map { it.ip }.forEach { ip ->
                    val level = adbShell(ip, "dumpsys battery")?.output?.let { QuestAdb.parseBatteryLevel(it) }
                    if (level != null) updateQuest(ip) { it.copy(battery = level) }
                }
            }
        }
    }

    /**
     * Starts a Demo Session through the Hub (adb + the Hub's start extra): [preset] 1..3, or one [demoId]
     * with [tutorial] "on" / "off" / null (descriptor default).
     */
    fun startSession(preset: Int? = null, demoId: String? = null, tutorial: String? = null) {
        val title = if (preset != null) "Hub のプリセット $preset" else DemoCatalog.labelFor(demoId) +
            when (tutorial) { "on" -> "（チュートリアルあり）"; "off" -> "（チュートリアルなし）"; else -> "" }
        startThroughHub(title, DemoCatalog.hubSessionCommand(preset, demoId, tutorial))
    }

    /** Starts a remote preset as one Hub session (`steps`). */
    fun startPreset(preset: RemotePreset) {
        val command = try { DemoCatalog.hubPlanCommand(preset.steps) } catch (_: IllegalArgumentException) {
            setNotice("プリセット「${preset.name}」を開始できません（デモ数・内容を確認してください）", true)
            return
        }
        startThroughHub(preset.name, command)
    }

    fun savePreset(index: Int?, preset: RemotePreset) {
        if (index != null && index in presets.indices) presets[index] = preset else presets.add(preset)
        settings.presets = presets.toList()
    }

    fun deletePreset(index: Int) {
        if (index !in presets.indices) return
        presets.removeAt(index)
        settings.presets = presets.toList()
    }

    private fun startThroughHub(title: String, command: String) {
        val quest = selectedQuest ?: return
        if (quest.adb != AdbState.CONNECTED) return
        viewModelScope.launch {
            val logId = addLog(quest.label, "開始 $title（Hub 経由）", null, LogState.SENT)
            val response = adbShell(quest.ip, command)
            if (response == null) {
                updateLog(logId) { it.copy(state = LogState.ERROR, detail = "adb 接続が切れました") }
                return@launch
            }
            val summary = response.allOutput.lineSequence().map { it.trim() }.filter { it.isNotEmpty() }.lastOrNull()?.take(120) ?: ""
            val ok = response.exitCode == 0 && !summary.startsWith("Error")
            updateLog(logId) {
                it.copy(state = if (ok) LogState.READY else LogState.FAILED, detail = "exit ${response.exitCode}${if (summary.isEmpty()) "" else " / $summary"}")
            }
            if (ok) {
                delay(LAUNCH_REDISCOVER_DELAY_MS)
                runDiscoveryRound()
            }
        }
    }

    /** Starts [app] through adb (fixed package table only). */
    fun launchApp(app: DemoApp) {
        val quest = selectedQuest ?: return
        if (quest.adb != AdbState.CONNECTED) return
        viewModelScope.launch {
            val logId = addLog(quest.label, "起動 ${app.label}（adb）", null, LogState.SENT)
            val response = adbShell(quest.ip, DemoCatalog.launchCommand(app))
            if (response == null) {
                updateLog(logId) { it.copy(state = LogState.ERROR, detail = "adb 接続が切れました") }
                return@launch
            }
            val summary = response.allOutput.lineSequence().map { it.trim() }.filter { it.isNotEmpty() }.lastOrNull()?.take(120) ?: ""
            val ok = response.exitCode == 0
            updateLog(logId) {
                it.copy(state = if (ok) LogState.READY else LogState.FAILED, detail = "exit ${response.exitCode}${if (summary.isEmpty()) "" else " / $summary"}")
            }
            if (ok) {
                delay(LAUNCH_REDISCOVER_DELAY_MS)
                runDiscoveryRound()
            }
        }
    }

    // ---- Wi-Fi adb over USB -------------------------------------------------------------------------

    fun openUsbDialog() {
        if (!usbAdbRunning) usbAdbStatus = "" to false
        usbDialogOpen = true
    }

    fun closeUsbDialog() {
        usbDialogOpen = false
    }

    /** The activity was opened (or re-delivered) by a USB_DEVICE_ATTACHED intent for an ADB device. */
    fun onUsbDeviceAttached() {
        if (!usbAdbRunning) usbAdbStatus = "Quest が USB で接続されました。「有効化」を押してください" to false
        usbDialogOpen = true
    }

    /**
     * Phone as USB host: runs `adb tcpip 5555` on a directly connected Quest, then adds / selects its
     * Wi-Fi IP and connects over Wi-Fi adb as usual.
     */
    fun enableWifiAdbOverUsb() {
        if (usbAdbRunning) return
        val app = getApplication<Application>()
        usbAdbRunning = true
        viewModelScope.launch {
            try {
                val manager = app.getSystemService(UsbManager::class.java)
                val device = manager?.let { UsbAdb.findDevice(it) }
                    ?: throw UsbAdbException("USB の Quest が見つかりません。直結し、スマホ側で「USB の制御: このデバイス」を選んでください")
                usbAdbStatus = "USB の使用を許可してください" to false
                if (!requestUsbPermission(manager, device)) throw UsbAdbException("USB の使用が許可されませんでした。もう一度押して許可してください")
                usbAdbStatus = "Quest と通信中…" to false
                val key = withContext(Dispatchers.IO) {
                    try {
                        keyPair // generates the key files on first use
                        UsbAdbKey.read(app.filesDir)
                    } catch (e: java.io.IOException) {
                        throw UsbAdbException("adb の鍵を読み込めません（${e.javaClass.simpleName}）")
                    } catch (e: java.security.GeneralSecurityException) {
                        throw UsbAdbException("adb の鍵を読み込めません（${e.javaClass.simpleName}）")
                    }
                }
                val ip = UsbAdb.enableWifiAdb(manager, device, key) {
                    viewModelScope.launch(Dispatchers.Main) {
                        usbAdbStatus = "ヘッドセット内で「このコンピューターから常に許可」にチェックして許可してください（最大 30 秒）" to false
                    }
                }
                onUsbWifiAdbEnabled(ip)
            } catch (e: UsbAdbException) {
                usbAdbStatus = (e.message ?: "") to true
                addLog("USB", "Wi-Fi adb を有効化", null, LogState.ERROR, e.message ?: "")
            } finally {
                usbAdbRunning = false
            }
        }
    }

    private suspend fun onUsbWifiAdbEnabled(ip: String) {
        if (quests.none { it.ip == ip }) {
            quests.add(QuestState(ip, nextLabel(), manual = true))
            persistQuests()
        }
        addLog(quests.first { it.ip == ip }.label, "USB で Wi-Fi adb を有効化 $ip", null, LogState.READY)
        // Any old Wi-Fi connection to this IP died with the adbd restart.
        adbJob?.cancel()
        adbConnections.remove(ip)?.close()
        updateQuest(ip) { it.copy(adb = AdbState.DISCONNECTED) }
        if (selectedIp != ip) {
            stopMirror()
            selectedIp = ip
            settings.selectedIp = ip
            mirrorVideoSize = null
        }
        wifiAdbOff = false
        usbAdbStatus = "有効にしました（$ip）。ケーブルを外して構いません" to false
        usbDialogOpen = false
        adbMessage = "Wi-Fi adb を有効にしました。接続します…"
        // adbd restarts in TCP mode; give it a moment before the Wi-Fi connect.
        delay(USB_TCPIP_SETTLE_MS)
        connectAdb()
    }

    private suspend fun requestUsbPermission(manager: UsbManager, device: UsbDevice): Boolean {
        if (manager.hasPermission(device)) return true
        val app = getApplication<Application>()
        val result = CompletableDeferred<Boolean>()
        val receiver = object : BroadcastReceiver() {
            override fun onReceive(context: Context, intent: Intent) {
                result.complete(intent.getBooleanExtra(UsbManager.EXTRA_PERMISSION_GRANTED, false))
            }
        }
        ContextCompat.registerReceiver(app, receiver, IntentFilter(ACTION_USB_PERMISSION), ContextCompat.RECEIVER_NOT_EXPORTED)
        try {
            // Mutable: the system adds EXTRA_DEVICE / EXTRA_PERMISSION_GRANTED. Explicit (package) as API 34 requires.
            val flags = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) PendingIntent.FLAG_MUTABLE else 0
            val intent = PendingIntent.getBroadcast(app, 0, Intent(ACTION_USB_PERMISSION).setPackage(app.packageName), flags)
            manager.requestPermission(device, intent)
            return withTimeoutOrNull(USB_PERMISSION_WAIT_MS) { result.await() } ?: false
        } finally {
            app.unregisterReceiver(receiver)
        }
    }

    // ---- mirror ------------------------------------------------------------------------------------

    fun changeMirrorEnabled(enabled: Boolean) {
        mirrorEnabled = enabled
        settings.mirrorEnabled = enabled
        if (!enabled) stopMirror()
        updateMirror()
    }

    fun updateMirrorSettings(value: MirrorSettings) {
        mirrorSettings = value
        settings.mirrorSettings = value
        if (mirrorSession != null) {
            stopMirror()
            updateMirror()
        }
    }

    fun onMirrorSurfaceAvailable(surface: Surface) {
        mirrorSurface = surface
        updateMirror()
    }

    /** Called from SurfaceHolder.Callback.surfaceDestroyed: the decoder must stop before returning. */
    fun onMirrorSurfaceDestroyed() {
        stopMirror()
        mirrorSurface = null
    }

    private fun updateMirror() {
        if (mirrorSession != null) return
        if (!mirrorEnabled) { mirrorStatus = "ミラー停止中"; return }
        if (!foreground) return
        val ip = selectedIp ?: run { mirrorStatus = "Quest を選択してください"; return }
        val surface = mirrorSurface ?: return
        if (adbConnections[ip]?.dadb == null || selectedQuest?.adb != AdbState.CONNECTED) { mirrorStatus = "adb 接続後に表示します"; return }
        val app = getApplication<Application>()
        mirrorStatus = "ミラー開始中…"
        var session: MirrorSession? = null
        session = MirrorSession({ Dadb.create(ip, QuestAdb.PORT, keyPair, QuestAdb.CONNECT_TIMEOUT_MS, 0) },
            { app.assets.open("scrcpy-server.jar") }, mirrorSettings, surface,
            object : MirrorSession.Listener {
                override fun onVideoSize(width: Int, height: Int) {
                    viewModelScope.launch(Dispatchers.Main) {
                        if (mirrorSession === session) { mirrorVideoSize = width to height; mirrorStatus = "" }
                    }
                }

                override fun onError(message: String) {
                    viewModelScope.launch(Dispatchers.Main) {
                        if (mirrorSession !== session) return@launch
                        mirrorStatus = "ミラーエラー: $message"
                        stopMirror()
                    }
                }
            })
        mirrorSession = session
        session!!.start()
    }

    private fun stopMirror() {
        mirrorSession?.stop()
        mirrorSession = null
    }

    fun retryMirror() {
        stopMirror()
        updateMirror()
    }

    // ---- display ---------------------------------------------------------------------------------

    fun setKeepScreenOnSetting(value: Boolean) {
        keepScreenOn = value
        settings.keepScreenOn = value
    }

    private companion object {
        const val DISCOVERY_INTERVAL_MS = 5_000L
        const val DISCOVERY_WINDOW_MS = 700L
        const val CONTROL_TIMEOUT_MS = 5_000L
        const val SWITCH_TIMEOUT_MS = 20_000L
        const val BATTERY_INTERVAL_MS = 30_000L
        const val LAUNCH_REDISCOVER_DELAY_MS = 3_000L
        const val MAX_LOGS = 20
        const val FOLLOW_AUTH_WAIT_MS = 3_000L
        const val QUERY_TIMEOUT_MS = 1_500L
        const val USB_PERMISSION_WAIT_MS = 60_000L
        const val USB_TCPIP_SETTLE_MS = 2_500L
        const val ACTION_USB_PERMISSION = "com.hapbeat.demoremote.USB_PERMISSION"
    }
}
