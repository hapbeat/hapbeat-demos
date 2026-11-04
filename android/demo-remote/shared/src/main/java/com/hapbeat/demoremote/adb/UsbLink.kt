package com.hapbeat.demoremote.adb

import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.security.PrivateKey
import javax.crypto.Cipher

/** A failure with a message that can be shown to the user as is. */
class UsbAdbException(message: String) : Exception(message)

/** What a USB step reports while it runs; every call may come from any thread. */
interface UsbProgress {
    fun status(text: String)
    /** One small line in the dialog: what is seen on USB. */
    fun deviceDetail(text: String)
    fun log(line: String)
}

/** The USB dialog's wording, which differs per platform. */
class UsbTexts(
    val dialogIntro: String,
    val dialogSteps: List<String>,
    val startLabel: String,
    val retryLabel: String,
    val startButtonWidthDp: Int,
    val settingsBody: String,
)

/** How this platform turns on a USB-connected Quest's Wi-Fi adb. */
interface UsbBridge {
    val texts: UsbTexts
    /** The USB link as far as the platform tells it, without blocking. */
    fun readLink(): UsbLink
    fun statusText(link: UsbLink): String
    /** The dialog's status area before a run: (text, isError). */
    fun idleStatus(link: UsbLink): Pair<String, Boolean>
    /** Until [stopWatch], [onEvent] runs on the main thread at each link change; `true`: a Quest was just plugged in. */
    fun startWatch(onEvent: (attached: Boolean) -> Unit)
    fun stopWatch()
    /** Called on the main thread. Throws [UsbAdbException] with a user-facing message. */
    suspend fun enableWifiAdb(link: UsbLink, progress: UsbProgress): UsbWifiAdbResult
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
    /** ro.serialno, so the entry is recognised when DHCP later gives the headset another IP ("" if unread). */
    val serial: String = "",
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
