package com.hapbeat.demoremote.adb

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.nio.file.Files
import java.util.concurrent.TimeUnit

/** One line of `adb devices -l`. */
data class AdbDeviceLine(val serial: String, val state: String, val model: String) {
    /** On a cable: not `ip:port`, not an mDNS `..._adb-tls-connect._tcp.` name. */
    val onUsb: Boolean get() = !serial.contains(':') && !serial.contains("._tcp")
}

object AdbOutput {
    private val MODEL = Regex("""\bmodel:(\S+)""")

    fun parseDevices(output: String): List<AdbDeviceLine> = output.lineSequence().drop(1).mapNotNull { line ->
        val parts = line.trim().split(Regex("""\s+"""))
        if (parts.size < 2) return@mapNotNull null
        AdbDeviceLine(parts[0], parts[1], MODEL.find(line)?.groupValues?.get(1).orEmpty())
    }.toList()

    /** `adb devices -l` writes the model with underscores: `Quest_3S`. */
    fun isQuest(device: AdbDeviceLine): Boolean = QuestAdb.isQuestModel(device.model)

    /**
     * The Quest to switch, or why there is none. A headset that has not approved this PC shows as `unauthorized`, without a
     * model, so such a device counts as "approval needed" only while no approved Quest is on the cable.
     */
    fun pickUsbQuest(devices: List<AdbDeviceLine>): AdbDeviceLine {
        val usb = devices.filter { it.onUsb }
        usb.firstOrNull { it.state == "device" && isQuest(it) }?.let { return it }
        if (usb.any { it.state == "unauthorized" }) throw UsbAdbException("ヘッドセットを被り、「USB デバッグを許可」で「常に許可」にチェックして許可してください。許可したらもう一度押してください")
        throw UsbAdbException("USB でつながった Quest がありません（データ通信できる USB-C ケーブルか、Quest の電源を確認）")
    }
}

/** Finds adb.exe; a client of another version than the running server would restart it and drop every Wi-Fi device. */
object AdbTool {
    fun locate(): File? = runningServer() ?: bundled() ?: androidSdk() ?: onPath()

    private fun runningServer(): File? = ProcessHandle.allProcesses()
        .map { it.info().command().orElse("") }
        .filter { it.endsWith("adb.exe", ignoreCase = true) }
        .map { File(it) }
        .filter { it.isFile }
        .findFirst().orElse(null)

    /** Copied into the app's resources at build time when the build machine had it. */
    private fun bundled(): File? = listOfNotNull(
        System.getProperty("compose.application.resources.dir"),
        System.getProperty("jpackage.app-path")?.let { File(it).parent },
    ).map { File(it, "adb.exe") }.firstOrNull { it.isFile }

    private fun androidSdk(): File? = listOfNotNull(
        System.getenv("ANDROID_HOME"), System.getenv("ANDROID_SDK_ROOT"),
        System.getenv("LOCALAPPDATA")?.let { "$it/Android/Sdk" },
        System.getProperty("user.home") + "/AppData/Local/Android/Sdk",
    ).map { File(it, "platform-tools/adb.exe") }.firstOrNull { it.isFile }

    private fun onPath(): File? = System.getenv("PATH").orEmpty().split(File.pathSeparator)
        .map { File(it, "adb.exe") }.firstOrNull { it.isFile }
}

/**
 * Turns on a USB-connected Quest's Wi-Fi adb with adb.exe, by the press of one button (no typed commands): finds the Quest,
 * reads its `wlan0` address and runs `tcpip 5555`; the normal Wi-Fi connect takes over from there.
 */
class DesktopUsbBridge : UsbBridge {
    override val texts = UsbTexts(
        dialogIntro = "Quest を再起動すると、PC からの Wi-Fi 接続（adb）が切れます。USB ケーブルで一度つなぐと元に戻ります",
        dialogSteps = listOf(
            "1. PC と Quest を USB-C ケーブル（データ対応）でつなぐ",
            "2. 下のボタンを押す。初めての PC では HMD を被り、「USB デバッグを許可」で「常に許可」にチェックして許可する",
            "3. 「完了」と出たらケーブルを外す",
        ),
        startLabel = "USB の Quest を Wi-Fi 接続に切替",
        retryLabel = "USB の Quest を Wi-Fi 接続に切替",
        startButtonWidthDp = 280,
        settingsBody = "Quest を再起動すると、PC からの Wi-Fi 接続（adb）が切れます。USB ケーブルで一度つなぐと元に戻ります。",
    )

    /** Asked only when the button is pressed: `adb devices` is not run in the background. */
    override fun readLink() = UsbLink.NONE
    override fun statusText(link: UsbLink) = "ボタンを押すと確認します"
    override fun idleStatus(link: UsbLink) = "Quest を USB でつないで、下のボタンを押してください" to false
    override fun startWatch(onEvent: (attached: Boolean) -> Unit) = Unit
    override fun stopWatch() = Unit

    override suspend fun enableWifiAdb(link: UsbLink, progress: UsbProgress): UsbWifiAdbResult = withContext(Dispatchers.IO) {
        val adb = AdbTool.locate() ?: throw UsbAdbException("adb.exe が見つかりません（Android platform-tools を入れてください）")
        progress.log("adb: ${adb.path}")
        progress.status("USB の Quest を探しています…")
        val listing = run(adb, "devices", "-l")
        progress.log(listing.lines().drop(1).filter { it.isNotBlank() }.joinToString(" / ").ifEmpty { "(機器なし)" })
        val quest = AdbOutput.pickUsbQuest(AdbOutput.parseDevices(listing))
        progress.deviceDetail("${quest.serial} ${quest.model}")
        progress.status("Quest と通信中…")
        val serial = run(adb, "-s", quest.serial, "shell", "getprop", "ro.serialno").trim()
        val tcpPort = run(adb, "-s", quest.serial, "shell", "getprop", "service.adb.tcp.port").trim()
        val inet = AdbProtocol.parseWlanInet(run(adb, "-s", quest.serial, "shell", "ip", "-4", "addr", "show", "wlan0"))
            ?: throw UsbAdbException("Quest に Wi-Fi のアドレスがありません。Quest を PC と同じ Wi-Fi につないでください")
        val ssid = AdbProtocol.parseWifiSsid(run(adb, "-s", quest.serial, "shell", "cmd", "wifi", "status"))
        progress.log("Quest wlan0 ${inet.address}/${inet.prefixLength} SSID ${ssid.ifEmpty { "(不明)" }}")
        // Already listening on 5555: skip tcpip so adbd is not restarted.
        if (AdbProtocol.isTcpPortSet(tcpPort, TCPIP_PORT)) {
            progress.log("tcpip は省略（すでに $TCPIP_PORT）")
            return@withContext UsbWifiAdbResult(inet.address, inet.prefixLength, ssid, alreadyEnabled = true, serial = serial)
        }
        val reply = run(adb, "-s", quest.serial, "tcpip", TCPIP_PORT.toString()).trim().take(80)
        progress.log("tcpip $TCPIP_PORT → ${reply.ifEmpty { "応答なし" }}")
        if (!reply.contains("restarting")) throw UsbAdbException("Wi-Fi adb を有効にできませんでした（${reply.ifEmpty { "応答なし" }}）")
        UsbWifiAdbResult(inet.address, inet.prefixLength, ssid, alreadyEnabled = false, serial = serial)
    }

    /**
     * Output of one adb.exe run. To a file, not a pipe: a server daemon that this client starts inherits the handle, and
     * reading a pipe would then never end.
     */
    private fun run(adb: File, vararg args: String): String {
        val output = Files.createTempFile("demo-remote-adb", ".txt").toFile()
        try {
            val process = ProcessBuilder(listOf(adb.path) + args).redirectErrorStream(true).redirectOutput(output).start()
            if (!process.waitFor(ADB_TIMEOUT_S, TimeUnit.SECONDS)) {
                process.destroyForcibly()
                throw UsbAdbException("adb が応答しません（${args.joinToString(" ")}）")
            }
            return output.readText()
        } catch (e: java.io.IOException) {
            throw UsbAdbException("adb.exe を実行できません（${e.javaClass.simpleName}）")
        } finally {
            output.delete()
        }
    }

    private companion object {
        const val TCPIP_PORT = 5555
        const val ADB_TIMEOUT_S = 20L
    }
}
