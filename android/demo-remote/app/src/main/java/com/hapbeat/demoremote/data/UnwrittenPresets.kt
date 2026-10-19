package com.hapbeat.demoremote.data

import com.hapbeat.demoremote.protocol.JsonArray
import com.hapbeat.demoremote.protocol.JsonException
import com.hapbeat.demoremote.protocol.JsonNumber
import com.hapbeat.demoremote.protocol.JsonObject
import com.hapbeat.demoremote.protocol.MiniJson

/**
 * A preset imported from a QR / link that is not written to a Hub yet, with the Hub slot (1..3) chosen for it
 * (demo-session.md「リモコンへのプリセット受け渡し」: kept on the phone until the Hub is in front and the user writes it).
 */
data class UnwrittenPreset(val id: Long, val preset: RemotePreset, val slot: Int)

/**
 * Storage form of the unwritten presets (one JSON string in the settings). Pure Kotlin so it runs in JVM unit tests.
 * Reading checks each entry again like an import (schema of one preset, allow list, option tables, slot 1..3); an
 * entry that no longer passes (for example a demo removed from this app) is dropped, the others are kept.
 */
object UnwrittenPresets {
    private const val VERSION = 1

    fun encode(items: List<UnwrittenPreset>): String = MiniJson.write(linkedMapOf(
        "version" to VERSION,
        "items" to items.map { linkedMapOf("id" to it.id, "slot" to it.slot, "preset" to PresetTransfer.presetJson(it.preset)) },
    ))

    fun decode(text: String): List<UnwrittenPreset> {
        if (text.isEmpty()) return emptyList()
        val root = try { MiniJson.parse(text) } catch (_: JsonException) { return emptyList() }
        val fields = (root as? JsonObject)?.fields ?: return emptyList()
        if ((fields["version"] as? JsonNumber)?.longOrNull() != VERSION.toLong()) return emptyList()
        val items = (fields["items"] as? JsonArray)?.items ?: return emptyList()
        val result = mutableListOf<UnwrittenPreset>()
        for (item in items) {
            val f = (item as? JsonObject)?.fields ?: continue
            val id = (f["id"] as? JsonNumber)?.longOrNull() ?: continue
            val slot = (f["slot"] as? JsonNumber)?.longOrNull()?.toInt()?.takeIf { it in HubPresets.NUMBERS } ?: continue
            val read = f["preset"]?.let(PresetTransfer::readPreset) ?: continue
            // Checked again; per-demo (device) options are taken out like an import.
            val preset = (PresetTransfer.checkDemos(listOf(read)) as? TransferResult.Accepted)?.presets?.single() ?: continue
            if (result.any { it.id == id }) continue
            result += UnwrittenPreset(id, preset, slot)
        }
        return result
    }

    /** Appends [presets] with their [slots] after [existing], with new ids. */
    fun add(existing: List<UnwrittenPreset>, presets: List<RemotePreset>, slots: List<Int>): List<UnwrittenPreset> {
        require(presets.size == slots.size)
        var id = (existing.maxOfOrNull { it.id } ?: 0L) + 1
        return existing + presets.mapIndexed { i, preset -> UnwrittenPreset(id++, preset, slots[i]) }
    }
}
