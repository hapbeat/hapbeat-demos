package com.hapbeat.demoremote.adb

import android.app.Application
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.hardware.usb.UsbDevice
import android.hardware.usb.UsbManager
import android.os.BatteryManager
import android.os.Build
import androidx.core.content.ContextCompat
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

/** The phone as USB host of the Quest ([UsbAdb]), instead of a PC. Starts by itself on plug-in. */
class AndroidUsbBridge(private val app: Application) : UsbBridge {
    private var usbWatch: BroadcastReceiver? = null

    override val texts = UsbTexts(
        dialogIntro = "Quest を再起動すると、スマホからの Wi-Fi 接続（adb）が切れます。USB ケーブルで一度つなぐと元に戻ります",
        dialogSteps = listOf(
            "1. スマホと Quest を USB-C ケーブル（データ対応）でつなぐ",
            "2. 自動で始まります。初めてのスマホでは HMD を被り、「USB デバッグを許可」で「常に許可」にチェックして許可する",
            "3. 「完了」と出たらケーブルを外す",
        ),
        startLabel = "始める",
        retryLabel = "もう一度試す",
        startButtonWidthDp = 120,
        settingsBody = "Quest を再起動すると、スマホからの Wi-Fi 接続（adb）が切れます。USB ケーブルで一度つなぐと元に戻ります（PC 不要）。",
    )

    /** Reads what the system tells about the USB link (attached devices, sticky USB_STATE, charging source). */
    override fun readLink(): UsbLink {
        val manager = app.getSystemService(UsbManager::class.java)
        val devices = manager?.deviceList?.values.orEmpty()
        val usbState = stickyBroadcast(ACTION_USB_STATE)
        val battery = stickyBroadcast(Intent.ACTION_BATTERY_CHANGED)
        return UsbLinkJudge.judge(
            adbDevice = devices.any { UsbAdb.adbInterface(it) != null },
            otherDevices = devices.count { UsbAdb.adbInterface(it) == null },
            usbConnected = usbState?.takeIf { it.hasExtra(EXTRA_USB_CONNECTED) }?.getBooleanExtra(EXTRA_USB_CONNECTED, false),
            hostConnected = usbState?.takeIf { it.hasExtra(EXTRA_USB_HOST_CONNECTED) }?.getBooleanExtra(EXTRA_USB_HOST_CONNECTED, false),
            pluggedUsb = battery?.takeIf { it.hasExtra(BatteryManager.EXTRA_PLUGGED) }
                ?.let { it.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0) == BatteryManager.BATTERY_PLUGGED_USB },
        )
    }

    override fun statusText(link: UsbLink): String = UsbLinkJudge.statusText(link)

    /** Status area before a run: the fix for a peripheral-side phone / USB debugging off, or how it starts. */
    override fun idleStatus(link: UsbLink): Pair<String, Boolean> = when (link) {
        UsbLink.PERIPHERAL, UsbLink.HOST_NO_ADB -> UsbLinkJudge.notFoundText(link) to true
        UsbLink.HOST_QUEST -> "Quest を認識しています。「始める」で始まります" to false
        UsbLink.NONE -> "Quest を USB でつなぐと始まります" to false
    }

    /** Current value of a sticky system broadcast, or null when it cannot be read (then nothing is inferred). */
    private fun stickyBroadcast(action: String): Intent? = try {
        app.registerReceiver(null, IntentFilter(action))
    } catch (_: SecurityException) {
        null
    }

    override fun startWatch(onEvent: (attached: Boolean) -> Unit) {
        if (usbWatch != null) return
        val receiver = object : BroadcastReceiver() {
            override fun onReceive(context: Context, intent: Intent) = onEvent(intent.action == UsbManager.ACTION_USB_DEVICE_ATTACHED)
        }
        val filter = IntentFilter().apply {
            addAction(UsbManager.ACTION_USB_DEVICE_ATTACHED)
            addAction(UsbManager.ACTION_USB_DEVICE_DETACHED)
            addAction(ACTION_USB_STATE)
            addAction(Intent.ACTION_POWER_CONNECTED)
            addAction(Intent.ACTION_POWER_DISCONNECTED)
        }
        // System broadcasts only: they are delivered to an exported or not-exported receiver alike.
        ContextCompat.registerReceiver(app, receiver, filter, ContextCompat.RECEIVER_EXPORTED)
        usbWatch = receiver
    }

    override fun stopWatch() {
        val receiver = usbWatch ?: return
        usbWatch = null
        runCatching { app.unregisterReceiver(receiver) }
    }

    /**
     * Phone as USB host: runs `adb tcpip 5555` on a directly connected Quest (skipped when already on).
     */
    override suspend fun enableWifiAdb(link: UsbLink, progress: UsbProgress): UsbWifiAdbResult {
        val manager = app.getSystemService(UsbManager::class.java) ?: throw UsbAdbException(UsbLinkJudge.notFoundText(UsbLink.NONE))
        val devices = UsbAdb.describeDevices(manager)
        progress.log("${UsbLinkJudge.statusText(link)}${if (devices.isEmpty()) "" else "（${devices.joinToString(" / ")}）"}")
        // Device details go to their own small line in the dialog (the three-line status area would cut them).
        progress.deviceDetail(devices.joinToString(" / "))
        val device = UsbAdb.findDevice(manager) ?: throw UsbAdbException(UsbLinkJudge.notFoundText(link))
        progress.status("USB の使用を許可してください")
        if (!requestUsbPermission(manager, device)) throw UsbAdbException("USB の使用が許可されませんでした。「もう一度試す」を押して許可してください")
        progress.status("Quest と通信中…")
        val key = withContext(Dispatchers.IO) {
            try {
                AdbKeys.load(app.filesDir) // generates the key files on first use
                UsbAdbKey.read(app.filesDir)
            } catch (e: java.io.IOException) {
                throw UsbAdbException("adb の鍵を読み込めません（${e.javaClass.simpleName}）")
            } catch (e: java.security.GeneralSecurityException) {
                throw UsbAdbException("adb の鍵を読み込めません（${e.javaClass.simpleName}）")
            }
        }
        return UsbAdb.enableWifiAdb(manager, device, key, onAuthWaiting = {
            progress.status("ヘッドセット内で「USB デバッグを許可」の「常に許可」にチェックして許可してください（最大 30 秒）")
        }, onStep = { line -> progress.log(line) })
    }

    private suspend fun requestUsbPermission(manager: UsbManager, device: UsbDevice): Boolean {
        if (manager.hasPermission(device)) return true
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

    private companion object {
        const val USB_PERMISSION_WAIT_MS = 60_000L
        const val ACTION_USB_PERMISSION = "com.hapbeat.demoremote.USB_PERMISSION"
        // Hidden UsbManager constants, written as strings.
        const val ACTION_USB_STATE = "android.hardware.usb.action.USB_STATE"
        const val EXTRA_USB_CONNECTED = "connected"
        const val EXTRA_USB_HOST_CONNECTED = "host_connected"
    }
}
