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
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.security.KeyFactory
import java.security.PrivateKey
import java.security.spec.PKCS8EncodedKeySpec
import java.util.Base64
import javax.crypto.Cipher

/** A failure with a message that can be shown to the user as is. */
class UsbAdbException(message: String) : Exception(message)

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

/** ADB wire format (pure Kotlin, unit-tested). */
object AdbProtocol {
    const val A_CNXN = 0x4e584e43
    const val A_AUTH = 0x48545541
    const val A_OPEN = 0x4e45504f
    const val A_OKAY = 0x59414b4f
    const val A_CLSE = 0x45534c43
    const val A_WRTE = 0x45545257

    const val VERSION = 0x01000001
    const val MAX_PAYLOAD = 256 * 1024
    const val AUTH_TOKEN = 1
    const val AUTH_SIGNATURE = 2
    const val AUTH_RSAPUBLICKEY = 3
    const val HEADER_SIZE = 24

    /** SHA-1 DigestInfo prefix: the token is signed as if it were a SHA-1 digest. */
    val SHA1_DIGEST_INFO = byteArrayOf(
        0x30, 0x21, 0x30, 0x09, 0x06, 0x05, 0x2b, 0x0e, 0x03, 0x02, 0x1a, 0x05, 0x00, 0x04, 0x14,
    )

    data class Header(val command: Int, val arg0: Int, val arg1: Int, val dataLength: Int, val dataCrc: Int, val magic: Int)

    /** Sum of the payload bytes (unsigned), as adb's "data_crc32". */
    fun checksum(payload: ByteArray): Int = payload.fold(0) { sum, b -> sum + (b.toInt() and 0xFF) }

    fun encodeHeader(command: Int, arg0: Int, arg1: Int, payload: ByteArray): ByteArray =
        ByteBuffer.allocate(HEADER_SIZE).order(ByteOrder.LITTLE_ENDIAN)
            .putInt(command).putInt(arg0).putInt(arg1).putInt(payload.size).putInt(checksum(payload)).putInt(command xor -1)
            .array()

    /** null when [bytes] is not a header (wrong size or magic). */
    fun decodeHeader(bytes: ByteArray): Header? {
        if (bytes.size != HEADER_SIZE) return null
        val b = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
        val header = Header(b.int, b.int, b.int, b.int, b.int, b.int)
        return header.takeIf { it.magic == it.command xor -1 && it.dataLength >= 0 }
    }

    /** DigestInfo + token: the block RSA PKCS#1 v1.5 signs for AUTH(SIGNATURE). */
    fun signatureInput(token: ByteArray): ByteArray = SHA1_DIGEST_INFO + token

    /** EMSA-PKCS1-v1_5 block: 00 01 FF..FF 00 DigestInfo token, [size] bytes (the RSA modulus length). */
    fun signatureBlock(token: ByteArray, size: Int): ByteArray {
        val input = signatureInput(token)
        return ByteArray(size) { 0xFF.toByte() }.also {
            it[0] = 0
            it[1] = 1
            it[size - input.size - 1] = 0
            input.copyInto(it, size - input.size)
        }
    }

    /**
     * PKCS#1 v1.5 signature over [signatureInput]. The padding is built here and the key is applied with
     * NoPadding, exactly like dadb's signPayload (proven against the Quest over Wi-Fi), rather than relying on
     * the provider's private-key PKCS1Padding mode.
     */
    fun sign(key: PrivateKey, token: ByteArray): ByteArray {
        val size = ((key as java.security.interfaces.RSAKey).modulus.bitLength() + 7) / 8
        return Cipher.getInstance("RSA/ECB/NoPadding").apply { init(Cipher.ENCRYPT_MODE, key) }.doFinal(signatureBlock(token, size))
    }

    /** "service" + NUL, as OPEN expects. */
    fun servicePayload(service: String): ByteArray = service.toByteArray(Charsets.UTF_8) + 0.toByte()

    /** `ip -4 addr show wlan0` -> the IPv4 address and prefix length, or null. */
    fun parseWlanInet(output: String): WlanInet? =
        Regex("""inet (\d{1,3}(?:\.\d{1,3}){3})/(\d{1,2})""").find(output)?.let {
            WlanInet(it.groupValues[1], it.groupValues[2].toInt())
        }

    /** SSID from `cmd wifi status` (the format varies by release, so it is read loosely); "" when not found. */
    fun parseWifiSsid(output: String): String {
        val ssid = Regex("""connected to "([^"]*)"""").find(output)?.groupValues?.get(1)
            ?: Regex("""SSID: "([^"]*)"""").find(output)?.groupValues?.get(1)
            ?: Regex("""(?<![A-Za-z])SSID: ([^,\r\n]+)""").find(output)?.groupValues?.get(1)?.trim()
        return ssid?.takeUnless { it.isEmpty() || it == "<unknown ssid>" } ?: ""
    }

    /** `getprop service.adb.tcp.port` already set to [port]: adbd listens on Wi-Fi, so `tcpip` can be skipped. */
    fun isTcpPortSet(getpropOutput: String, port: Int): Boolean = getpropOutput.trim() == port.toString()
}

data class WlanInet(val address: String, val prefixLength: Int)

/** What the USB step found and did, for the log and the subnet check. */
data class UsbWifiAdbResult(
    val ip: String,
    val prefixLength: Int,
    /** "" when it could not be read. */
    val ssid: String,
    /** `tcpip` was skipped because service.adb.tcp.port was already 5555. */
    val alreadyEnabled: Boolean,
)

/** The phone's side of the USB link, as far as the system tells it. */
enum class UsbLink { HOST_QUEST, HOST_NO_ADB, PERIPHERAL, NONE }

object UsbLinkJudge {
    /**
     * [adbDevice]: an attached device exposes an ADB interface. [otherDevices]: attached devices without one.
     * [usbConnected] / [hostConnected]: extras "connected" / "host_connected" of the sticky USB_STATE
     * (null when missing). [pluggedUsb]: BATTERY_PLUGGED_USB, used only when USB_STATE does not decide it.
     */
    fun judge(adbDevice: Boolean, otherDevices: Int, usbConnected: Boolean?, hostConnected: Boolean?, pluggedUsb: Boolean?): UsbLink =
        when {
            adbDevice -> UsbLink.HOST_QUEST
            otherDevices > 0 -> UsbLink.HOST_NO_ADB
            usbConnected == false -> UsbLink.NONE
            usbConnected == true && hostConnected == false -> UsbLink.PERIPHERAL
            usbConnected == true && hostConnected == true -> UsbLink.NONE
            pluggedUsb == true -> UsbLink.PERIPHERAL
            else -> UsbLink.NONE
        }

    /** Fixed one-line status in the dialog. */
    fun statusText(link: UsbLink): String = when (link) {
        UsbLink.HOST_QUEST -> "スマホがホスト（Quest を認識）"
        UsbLink.HOST_NO_ADB -> "Quest は見えるが USB デバッグ無効"
        UsbLink.PERIPHERAL -> "スマホが周辺機器側（Quest 側がホスト）"
        UsbLink.NONE -> "未接続"
    }

    /** Why no ADB device can be used, shown when the USB step cannot start. */
    fun notFoundText(link: UsbLink): String = when (link) {
        UsbLink.HOST_NO_ADB -> "Quest は見えていますが USB デバッグが無効です（Quest の開発者モードを確認）"
        UsbLink.PERIPHERAL -> "スマホが周辺機器側になっています。ケーブルを挿し直すか、スマホの通知の『USB の制御』で『このデバイス』を選んでください"
        UsbLink.HOST_QUEST, UsbLink.NONE -> "Quest が見つかりません（データ通信できる USB-C ケーブルか、Quest の電源を確認）"
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
         * Connects over USB, reads the Wi-Fi IP / prefix / SSID and runs `tcpip:5555` unless adbd already
         * listens on it. [onStep] gets one line per step (called on the IO thread).
         * Throws [UsbAdbException] with a user-facing message.
         */
        suspend fun enableWifiAdb(
            manager: UsbManager, device: UsbDevice, key: UsbAdbKey, onAuthWaiting: () -> Unit, onStep: (String) -> Unit,
        ): UsbWifiAdbResult =
            withContext(Dispatchers.IO) {
                try {
                    open(manager, device).use { adb ->
                        adb.connect(key, onAuthWaiting)
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
                            return@use UsbWifiAdbResult(inet.address, inet.prefixLength, ssid, alreadyEnabled = true)
                        }
                        val reply = adb.enableTcpip(TCPIP_PORT).trim().take(80)
                        onStep("tcpip:$TCPIP_PORT → ${reply.ifEmpty { "応答なし" }}")
                        if (!reply.contains("restarting")) {
                            throw UsbAdbException("Wi-Fi adb を有効にできませんでした（${reply.ifEmpty { "応答なし" }}）")
                        }
                        UsbWifiAdbResult(inet.address, inet.prefixLength, ssid, alreadyEnabled = false)
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
