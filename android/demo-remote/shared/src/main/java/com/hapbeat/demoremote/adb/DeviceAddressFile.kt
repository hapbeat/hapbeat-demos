package com.hapbeat.demoremote.adb

import com.hapbeat.demoremote.DemoApp
import com.hapbeat.demoremote.DemoCatalog
import com.hapbeat.demoremote.protocol.JsonException
import com.hapbeat.demoremote.protocol.JsonNumber
import com.hapbeat.demoremote.protocol.JsonObject
import com.hapbeat.demoremote.protocol.MiniJson

/** A headset's Hapbeat address: [player] / [group] are 1..99 or [DeviceAddressFile.UNSET]. */
data class DeviceAddress(val player: Int, val group: Int)

/** One demo's `hapbeat-device.json` as read with `cat`. */
sealed interface DeviceAddressRead {
    /** No file: the demo applies no address of its own (same as both axes unset). */
    data object Missing : DeviceAddressRead
    /** The file is there but not a valid address file, or `cat` failed otherwise. */
    data object Invalid : DeviceAddressRead
    data class Value(val address: DeviceAddress) : DeviceAddressRead
}

/**
 * What the demos of one headset hold: [primary] is the file of [primaryPackage] (the Hub, else the first demo found),
 * [mixed] when the demos differ.
 */
data class DeviceAddressSummary(val primaryPackage: String, val primary: DeviceAddressRead, val mixed: Boolean)

/**
 * The per-headset address file of every demo runtime (demo-session.md「端末ごとの Hapbeat 宛先」):
 * `/sdcard/Android/data/<package>/files/hapbeat-device.json`, read once when the demo starts. Written over adb the way
 * `tools/install-demos.ps1` does (`mkdir -p`, write, read back). Pure Kotlin so the rules run in JVM unit tests.
 */
object DeviceAddressFile {
    const val FILE_NAME = "hapbeat-device.json"
    const val UNSET = -1
    val VALUES = 1..99

    fun isValid(value: Int): Boolean = value == UNSET || value in VALUES

    fun dir(packageName: String): String = "/sdcard/Android/data/$packageName/files"

    fun path(packageName: String): String = "${dir(packageName)}/$FILE_NAME"

    /** The file content, as install-demos.ps1 writes it: one JSON line and a newline. */
    fun content(address: DeviceAddress): String {
        require(isValid(address.player) && isValid(address.group)) { "address out of range: $address" }
        return "{\"version\":1,\"player\":${address.player},\"group\":${address.group}}\n"
    }

    fun mkdirCommand(packageName: String): String = "mkdir -p ${dir(packageName)}"

    /** Writes [content] with printf (the JSON has no `'`, `%` or `\`, so it goes in single quotes as is). */
    fun writeCommand(packageName: String, address: DeviceAddress): String =
        "printf '%s\\n' '${content(address).trimEnd('\n')}' > ${path(packageName)}"

    fun readCommand(packageName: String): String = "cat ${path(packageName)}"

    /** `cat` result ([exitCode], stdout + stderr) -> what the file holds. */
    fun parse(exitCode: Int, output: String): DeviceAddressRead {
        if (exitCode != 0) return if (output.contains("No such file", ignoreCase = true)) DeviceAddressRead.Missing else DeviceAddressRead.Invalid
        val json = try {
            MiniJson.parse(output.trim()) as? JsonObject
        } catch (_: JsonException) {
            null
        } ?: return DeviceAddressRead.Invalid
        if (json.fields.keys != setOf("version", "player", "group")) return DeviceAddressRead.Invalid
        val axis = { key: String -> (json.fields[key] as? JsonNumber)?.longOrNull()?.takeIf { it in Int.MIN_VALUE..Int.MAX_VALUE }?.toInt() }
        if (axis("version") != 1) return DeviceAddressRead.Invalid
        val player = axis("player")?.takeIf(::isValid) ?: return DeviceAddressRead.Invalid
        val group = axis("group")?.takeIf(::isValid) ?: return DeviceAddressRead.Invalid
        return DeviceAddressRead.Value(DeviceAddress(player, group))
    }

    /** The address a demo applies: a missing file is both axes unset; null for an invalid file. */
    fun effective(read: DeviceAddressRead): DeviceAddress? = when (read) {
        DeviceAddressRead.Missing -> DeviceAddress(UNSET, UNSET)
        DeviceAddressRead.Invalid -> null
        is DeviceAddressRead.Value -> read.address
    }

    /** The known demos (Hub included) of [installed] (`pm list packages`), in the catalog order: the files to write / read. */
    fun targets(installed: Set<String>): List<DemoApp> =
        DemoCatalog.apps.filter { it.packageName.isNotEmpty() && it.packageName in installed }.distinctBy { it.packageName }

    /** The read-back after a write holds [written]. */
    fun verified(written: DeviceAddress, read: DeviceAddressRead): Boolean = read == DeviceAddressRead.Value(written)

    /** "書けた 7 本 / 失敗 1 本（Boxing）" (the failure part only when something failed). */
    fun resultText(written: Int, failed: List<String>): String =
        "書けた $written 本" + if (failed.isEmpty()) "" else " / 失敗 ${failed.size} 本（${failed.joinToString("、")}）"

    /** [reads] (package -> read) of one headset -> the Hub's (else the first) and whether they differ; null when empty. */
    fun summarize(reads: List<Pair<String, DeviceAddressRead>>): DeviceAddressSummary? {
        if (reads.isEmpty()) return null
        val primary = reads.firstOrNull { it.first == DemoCatalog.HUB_PACKAGE } ?: reads.first()
        // An invalid file counts as a value of its own.
        val mixed = reads.map { effective(it.second) }.distinct().size > 1
        return DeviceAddressSummary(primary.first, primary.second, mixed)
    }

    /**
     * The Hub's HUB_SETTINGS player / group ([hub], what it read at its start) differ from its file read over adb: the Hub
     * has not been restarted since the file changed. False when either is unknown.
     */
    fun hubIsStale(summary: DeviceAddressSummary?, hub: DeviceAddress?): Boolean {
        if (summary == null || hub == null || summary.primaryPackage != DemoCatalog.HUB_PACKAGE) return false
        val file = effective(summary.primary) ?: return false
        return file != hub
    }

    /** [value] after −／＋ ([delta] -1 / +1): 指定なし below 1, 1 above 指定なし, kept at 99. */
    fun step(value: Int, delta: Int): Int {
        val next = if (value == UNSET) (if (delta > 0) VALUES.first else UNSET) else value + delta
        return when {
            next < VALUES.first -> UNSET
            next > VALUES.last -> VALUES.last
            else -> next
        }
    }

    /** "3" / "指定なし" for one axis. */
    fun axisText(value: Int): String = if (value == UNSET) "指定なし" else value.toString()

    /** Status line of the selected HMD: "グループ 2", "グループ 指定なし", "グループ 混在", "グループ --" (not read / unreadable). */
    fun groupStatus(summary: DeviceAddressSummary?): String {
        if (summary == null) return "グループ --"
        if (summary.mixed) return "グループ 混在"
        val address = effective(summary.primary) ?: return "グループ --"
        return "グループ ${axisText(address.group)}"
    }
}
