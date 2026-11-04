package com.hapbeat.demoremote.adb

import android.hardware.usb.UsbConstants
import android.hardware.usb.UsbDevice
import android.hardware.usb.UsbDeviceConnection
import android.hardware.usb.UsbEndpoint
import android.hardware.usb.UsbInterface
import android.hardware.usb.UsbManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.Closeable
import java.io.File
import java.security.KeyFactory
import java.security.PrivateKey
import java.security.spec.PKCS8EncodedKeySpec
import java.util.Base64

/** The app's adb key as files: the same pair dadb uses for Wi-Fi adb (see [AdbKeys]). */
class UsbAdbKey(val privateKey: PrivateKey, val publicKey: ByteArray) {
    companion object {
        /** Reads filesDir/adbkey (PEM PKCS#8) and adbkey.pub. Call after [AdbKeys.load] has created them. */
        fun read(filesDir: File): UsbAdbKey {
            val pem = File(filesDir, "adbkey").readText()
            val der = Base64.getMimeDecoder().decode(
                pem.replace("-----BEGIN PRIVATE KEY-----", "").replace("-----END PRIVATE KEY-----", "").trim(),
            )
            val key = KeyFactory.getInstance("RSA").generatePrivate(PKCS8EncodedKeySpec(der))
            return UsbAdbKey(key, File(filesDir, "adbkey.pub").readBytes())
        }
    }
}

/**
 * Minimal adb host over the Android USB Host API: enough to read the Quest's Wi-Fi IP and run
 * `tcpip:5555` with the phone acting as the USB host. All reads are bounded by deadlines.
 */
class UsbAdb private constructor(
    private val connection: UsbDeviceConnection,
    private val intf: UsbInterface,
    private val inEp: UsbEndpoint,
    private val outEp: UsbEndpoint,
) : Closeable {
    private var nextLocalId = 1

    /** CNXN + AUTH. [onAuthWaiting] is called when the headset has to accept this phone's key. */
    fun connect(key: UsbAdbKey, onAuthWaiting: () -> Unit) {
        write(AdbProtocol.A_CNXN, AdbProtocol.VERSION, AdbProtocol.MAX_PAYLOAD, "host::\u0000".toByteArray(Charsets.UTF_8))
        var signed = false
        var deadline = now() + HANDSHAKE_TIMEOUT_MS
        while (true) {
            val (header, payload) = read(deadline)
                ?: throw UsbAdbException(if (signed) "ヘッドセット内で許可されませんでした（30 秒）。もう一度押して許可してください" else "Quest から応答がありません")
            when (header.command) {
                AdbProtocol.A_CNXN -> return
                AdbProtocol.A_AUTH -> {
                    if (header.arg0 != AdbProtocol.AUTH_TOKEN) continue
                    if (!signed) {
                        write(AdbProtocol.A_AUTH, AdbProtocol.AUTH_SIGNATURE, 0, AdbProtocol.sign(key.privateKey, payload))
                        signed = true
                    } else {
                        // Key not trusted yet: offer it; the headset shows the USB debugging dialog.
                        write(AdbProtocol.A_AUTH, AdbProtocol.AUTH_RSAPUBLICKEY, 0, key.publicKey + 0.toByte())
                        onAuthWaiting()
                        deadline = now() + AUTH_WAIT_MS
                    }
                }
            }
        }
    }

    /** Runs [service] (e.g. "shell:…") and returns its output; [allowDrop] tolerates the device going away mid-way. */
    fun runService(service: String, allowDrop: Boolean = false): String {
        val localId = nextLocalId++
        write(AdbProtocol.A_OPEN, localId, 0, AdbProtocol.servicePayload(service))
        val deadline = now() + SERVICE_TIMEOUT_MS
        val output = StringBuilder()
        var remoteId = 0
        while (true) {
            val message = try {
                read(deadline)
            } catch (e: UsbAdbException) {
                if (allowDrop && output.isNotEmpty()) return output.toString() else throw e
            }
            if (message == null) {
                if (allowDrop && output.isNotEmpty()) return output.toString()
                throw UsbAdbException("Quest から応答がありません（$service）")
            }
            val (header, payload) = message
            if (header.arg1 != localId) continue
            when (header.command) {
                AdbProtocol.A_OKAY -> remoteId = header.arg0
                AdbProtocol.A_WRTE -> {
                    remoteId = header.arg0
                    output.append(String(payload, Charsets.UTF_8))
                    write(AdbProtocol.A_OKAY, localId, remoteId, ByteArray(0), allowFail = allowDrop)
                }
                AdbProtocol.A_CLSE -> {
                    if (remoteId != 0) write(AdbProtocol.A_CLSE, localId, remoteId, ByteArray(0), allowFail = true)
                    return output.toString()
                }
            }
        }
    }

    /** The headset's Wi-Fi IPv4 and prefix. */
    fun wlanInet(): WlanInet? = AdbProtocol.parseWlanInet(runService("shell:ip -4 addr show wlan0"))

    /** `adb tcpip [port]`: adbd restarts in TCP mode and this USB connection ends. */
    fun enableTcpip(port: Int): String = runService("tcpip:$port", allowDrop = true)

    override fun close() {
        try {
            connection.releaseInterface(intf)
        } catch (_: Exception) {
            // The device may already be gone (adbd restarted by tcpip).
        }
        connection.close()
    }

    private fun write(command: Int, arg0: Int, arg1: Int, payload: ByteArray, allowFail: Boolean = false) {
        val ok = bulkOut(AdbProtocol.encodeHeader(command, arg0, arg1, payload)) && (payload.isEmpty() || bulkOut(payload))
        if (!ok && !allowFail) throw UsbAdbException("USB への書き込みに失敗しました。ケーブルを挿し直してください")
    }

    private fun bulkOut(data: ByteArray): Boolean {
        var offset = 0
        while (offset < data.size) {
            // The offset overload of bulkTransfer is API 28+; minSdk 26 goes through a chunk copy.
            val chunk = data.copyOfRange(offset, minOf(data.size, offset + TRANSFER_CHUNK))
            val n = connection.bulkTransfer(outEp, chunk, chunk.size, WRITE_TIMEOUT_MS)
            if (n <= 0) return false
            offset += n
        }
        return true
    }

    /** Next message, or null when [deadline] passes. bulkTransfer returns -1 on both timeout and error. */
    private fun read(deadline: Long): Pair<AdbProtocol.Header, ByteArray>? {
        val headerBytes = ByteArray(AdbProtocol.HEADER_SIZE)
        while (true) {
            if (now() > deadline) return null
            val n = connection.bulkTransfer(inEp, headerBytes, headerBytes.size, READ_POLL_MS)
            if (n == AdbProtocol.HEADER_SIZE) break
            // n <= 0: timeout / zero-length packet; keep polling until the deadline.
        }
        val header = AdbProtocol.decodeHeader(headerBytes) ?: throw UsbAdbException("Quest から不正な応答を受け取りました")
        val payload = ByteArray(header.dataLength)
        var offset = 0
        while (offset < payload.size) {
            if (now() > deadline) return null
            val chunk = ByteArray(minOf(TRANSFER_CHUNK, payload.size - offset))
            val n = connection.bulkTransfer(inEp, chunk, chunk.size, READ_POLL_MS)
            if (n > 0) {
                chunk.copyInto(payload, offset, 0, n)
                offset += n
            }
        }
        return header to payload
    }

    companion object {
        const val TCPIP_PORT = 5555
        const val AUTH_WAIT_MS = 30_000L
        private const val HANDSHAKE_TIMEOUT_MS = 5_000L
        private const val SERVICE_TIMEOUT_MS = 10_000L
        private const val READ_POLL_MS = 500
        private const val WRITE_TIMEOUT_MS = 2_000
        private const val TRANSFER_CHUNK = 16_384

        private fun now() = System.currentTimeMillis()

        /** The ADB interface (class 0xFF / subclass 0x42 / protocol 0x01) of [device], or null. */
        fun adbInterface(device: UsbDevice): UsbInterface? =
            (0 until device.interfaceCount).map { device.getInterface(it) }.firstOrNull {
                it.interfaceClass == 0xFF && it.interfaceSubclass == 0x42 && it.interfaceProtocol == 0x01
            }

        /** First attached device that exposes an ADB interface. */
        fun findDevice(manager: UsbManager): UsbDevice? = manager.deviceList.values.firstOrNull { adbInterface(it) != null }

        /** "VID:PID 2833:0183 if ff/42/01, 08/06/50" per attached device: what the phone sees on USB. */
        fun describeDevices(manager: UsbManager): List<String> = manager.deviceList.values.map { d ->
            val interfaces = (0 until d.interfaceCount).map { d.getInterface(it) }.joinToString(", ") {
                "%02x/%02x/%02x".format(it.interfaceClass, it.interfaceSubclass, it.interfaceProtocol)
            }
            "VID:PID %04x:%04x if %s".format(d.vendorId, d.productId, interfaces.ifEmpty { "-" })
        }

        /** Opens and claims the ADB interface; the caller must already hold USB permission for [device]. */
        fun open(manager: UsbManager, device: UsbDevice): UsbAdb {
            val intf = adbInterface(device) ?: throw UsbAdbException("USB デバッグのインターフェースがありません（Quest の開発者モードを確認）")
            val endpoints = (0 until intf.endpointCount).map { intf.getEndpoint(it) }.filter { it.type == UsbConstants.USB_ENDPOINT_XFER_BULK }
            val inEp = endpoints.firstOrNull { it.direction == UsbConstants.USB_DIR_IN }
            val outEp = endpoints.firstOrNull { it.direction == UsbConstants.USB_DIR_OUT }
            if (inEp == null || outEp == null) throw UsbAdbException("USB デバッグのインターフェースがありません（Quest の開発者モードを確認）")
            val connection = manager.openDevice(device) ?: throw UsbAdbException("USB デバイスを開けません。ケーブルを挿し直してください")
            if (!connection.claimInterface(intf, true)) {
                connection.close()
                throw UsbAdbException("USB デバッグのインターフェースを使えません（PC の adb など他のアプリが使用中の可能性）")
            }
            return UsbAdb(connection, intf, inEp, outEp)
        }

        /**
         * Connects over USB, checks the model is a Quest, reads the Wi-Fi IP / prefix / SSID and runs
         * `tcpip:5555` unless adbd already listens on it. [onStep] gets one line per step (called on the IO thread).
         * Throws [UsbAdbException] with a user-facing message.
         */
        suspend fun enableWifiAdb(
            manager: UsbManager, device: UsbDevice, key: UsbAdbKey, onAuthWaiting: () -> Unit, onStep: (String) -> Unit,
        ): UsbWifiAdbResult =
            withContext(Dispatchers.IO) {
                try {
                    open(manager, device).use { adb ->
                        adb.connect(key, onAuthWaiting)
                        // Any Android with USB debugging matches the ADB filter: never run tcpip on a non-Quest.
                        val model = QuestAdb.parseModel(adb.runService("shell:getprop ro.product.model"))
                        onStep("getprop ro.product.model = ${model.ifEmpty { "(空)" }}")
                        if (!QuestAdb.isQuestModel(model)) throw UsbAdbException("Quest ではありません（${model.ifEmpty { "機種不明" }}）")
                        val serial = adb.runService("shell:getprop ro.serialno").trim()
                        onStep("getprop ro.serialno = ${serial.ifEmpty { "(空)" }}")
                        val tcpPort = adb.runService("shell:getprop service.adb.tcp.port").trim()
                        onStep("getprop service.adb.tcp.port = ${tcpPort.ifEmpty { "(空)" }}")
                        val inet = adb.wlanInet() ?: throw UsbAdbException("Quest の Wi-Fi IP を取得できません。Quest が Wi-Fi に繋がっているか確認してください")
                        // Best effort: the SSID only helps the diagnosis.
                        val ssid = try {
                            AdbProtocol.parseWifiSsid(adb.runService("shell:cmd wifi status"))
                        } catch (_: UsbAdbException) {
                            ""
                        }
                        onStep("Quest wlan0 ${inet.address}/${inet.prefixLength} SSID ${ssid.ifEmpty { "(不明)" }}")
                        // Already listening on 5555: skip tcpip so adbd is not restarted.
                        if (AdbProtocol.isTcpPortSet(tcpPort, TCPIP_PORT)) {
                            onStep("tcpip は省略（すでに $TCPIP_PORT）")
                            return@use UsbWifiAdbResult(inet.address, inet.prefixLength, ssid, alreadyEnabled = true, serial = serial)
                        }
                        val reply = adb.enableTcpip(TCPIP_PORT).trim().take(80)
                        onStep("tcpip:$TCPIP_PORT → ${reply.ifEmpty { "応答なし" }}")
                        if (!reply.contains("restarting")) {
                            throw UsbAdbException("Wi-Fi adb を有効にできませんでした（${reply.ifEmpty { "応答なし" }}）")
                        }
                        UsbWifiAdbResult(inet.address, inet.prefixLength, ssid, alreadyEnabled = false, serial = serial)
                    }
                } catch (e: UsbAdbException) {
                    throw e
                } catch (e: kotlinx.coroutines.CancellationException) {
                    throw e
                } catch (e: Exception) {
                    throw UsbAdbException("USB adb でエラーが発生しました（${e.javaClass.simpleName}）")
                }
            }
    }
}
