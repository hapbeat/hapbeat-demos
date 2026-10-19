package com.hapbeat.demoremote.data

import com.hapbeat.demoremote.DemoCatalog
import com.hapbeat.demoremote.protocol.DemoSwitchProtocol
import com.hapbeat.demoremote.protocol.JsonException
import com.hapbeat.demoremote.protocol.JsonNumber
import com.hapbeat.demoremote.protocol.JsonObject
import com.hapbeat.demoremote.protocol.JsonString
import com.hapbeat.demoremote.protocol.MiniJson

/**
 * One demo's per-demo settings (demo-session.md「デモごとの設定」) as the phone keeps them for one Quest: [hub] the values last
 * read from or written to that Quest's Hub (DEMO_SETTINGS), [unwritten] changes saved on the phone that the Hub does not
 * have yet (the Hub was not in front or did not answer). They are written when the Hub is in front again and the user
 * asks (DEMO_SETTINGS_SET).
 */
data class DemoSettingsEntry(val hub: Map<String, String> = emptyMap(), val unwritten: Map<String, String> = emptyMap()) {
    /** What the phone shows: the Hub's values with the unwritten changes over them. */
    val values: Map<String, String> get() = hub + unwritten
}

/** Unwritten changes of one demo on one Quest ([quest]: a [DemoSettingsStore.questKey]). */
data class UnwrittenDemoSettings(val quest: String, val demoId: String, val settings: Map<String, String>)

/**
 * The phone's copy of each Quest's per-demo settings: Quest key ([questKey]) → demo ID → entry. Kept in the settings
 * (one JSON string), so unwritten changes survive closing the app. Pure Kotlin so it runs in JVM unit tests. Only
 * demos and options of this app's table with their listed values are kept; anything else read back is dropped.
 */
object DemoSettingsStore {
    private const val VERSION = 1

    /** A Quest by its adb serial when known, else by its IP (as the remembered selection). */
    fun questKey(serial: String, ip: String): String = if (serial.isNotEmpty()) "serial:$serial" else "ip:$ip"

    /** The keys [entry] / [unwritten] look under for a Quest: its serial first, then its IP (saved before the serial was known). */
    fun questKeys(serial: String, ip: String): List<String> = listOfNotNull(serial.takeIf { it.isNotEmpty() }?.let { "serial:$it" }, "ip:$ip")

    fun entry(book: Map<String, Map<String, DemoSettingsEntry>>, keys: List<String>, demoId: String): DemoSettingsEntry? =
        keys.firstNotNullOfOrNull { book[it]?.get(demoId) }

    /**
     * DEMO_SETTINGS of [demoId] read from the Quest's Hub: [settings] becomes the Hub's values (only the options of this
     * app's table); unwritten changes stay, except those the Hub already has.
     */
    fun onRead(
        book: Map<String, Map<String, DemoSettingsEntry>>, keys: List<String>, demoId: String, settings: Map<String, String>,
    ): Map<String, Map<String, DemoSettingsEntry>> {
        val hub = known(demoId, settings)
        val old = entry(book, keys, demoId) ?: DemoSettingsEntry()
        return put(book, keys, demoId, DemoSettingsEntry(hub, old.unwritten.filter { (id, value) -> hub[id] != value }))
    }

    /**
     * 保存: [edited] (the values shown, every option or only some) becomes the unwritten change, as far as it differs from
     * the Hub's values (all of it while those are unknown). Changes back to the Hub's value cancel the unwritten one.
     */
    fun save(
        book: Map<String, Map<String, DemoSettingsEntry>>, keys: List<String>, demoId: String, edited: Map<String, String>,
    ): Map<String, Map<String, DemoSettingsEntry>> {
        val old = entry(book, keys, demoId) ?: DemoSettingsEntry()
        val changed = known(demoId, edited).filter { (id, value) -> old.hub[id] != value }
        return put(book, keys, demoId, old.copy(unwritten = changed))
    }

    /** The Hub took [written] (READY): they are the Hub's values now and no longer unwritten (later changes stay). */
    fun onWritten(
        book: Map<String, Map<String, DemoSettingsEntry>>, keys: List<String>, demoId: String, written: Map<String, String>,
    ): Map<String, Map<String, DemoSettingsEntry>> {
        val old = entry(book, keys, demoId) ?: DemoSettingsEntry()
        return put(book, keys, demoId, DemoSettingsEntry(old.hub + written, old.unwritten.filter { (id, value) -> written[id] != value }))
    }

    /** Every unwritten change of the Quest with [keys], in catalog order. */
    fun unwritten(book: Map<String, Map<String, DemoSettingsEntry>>, keys: List<String>): List<UnwrittenDemoSettings> =
        DemoCatalog.sessionApps.mapNotNull { app ->
            val key = keys.firstOrNull { book[it]?.get(app.demoId) != null } ?: return@mapNotNull null
            val changes = book[key]!![app.demoId]!!.unwritten
            if (changes.isEmpty()) null else UnwrittenDemoSettings(key, app.demoId, changes)
        }

    /** Stores [entry] under the first (primary) key and removes the demo from the other keys of the same Quest. */
    private fun put(
        book: Map<String, Map<String, DemoSettingsEntry>>, keys: List<String>, demoId: String, entry: DemoSettingsEntry,
    ): Map<String, Map<String, DemoSettingsEntry>> {
        val result = book.mapValues { (key, demos) -> if (key in keys) demos - demoId else demos }.toMutableMap()
        result[keys.first()] = (result[keys.first()] ?: emptyMap()) + (demoId to entry)
        return result.filterValues { it.isNotEmpty() }
    }

    /** Only options of [demoId]'s table with one of their values. */
    private fun known(demoId: String, values: Map<String, String>): Map<String, String> {
        val table = DemoCatalog.optionsFor(demoId)
        return values.filter { (id, value) -> table.any { o -> o.id == id && o.values.any { it.value == value } } }
    }

    fun encode(book: Map<String, Map<String, DemoSettingsEntry>>): String = MiniJson.write(linkedMapOf(
        "version" to VERSION,
        "quests" to book.toSortedMap().mapValues { (_, demos) ->
            demos.toSortedMap().mapValues { (_, entry) ->
                linkedMapOf("hub" to entry.hub.toSortedMap(), "unwritten" to entry.unwritten.toSortedMap())
            }
        },
    ))

    fun decode(text: String): Map<String, Map<String, DemoSettingsEntry>> {
        if (text.isEmpty()) return emptyMap()
        val root = try { MiniJson.parse(text) } catch (_: JsonException) { return emptyMap() }
        val fields = (root as? JsonObject)?.fields ?: return emptyMap()
        if ((fields["version"] as? JsonNumber)?.longOrNull() != VERSION.toLong()) return emptyMap()
        val quests = (fields["quests"] as? JsonObject)?.fields ?: return emptyMap()
        val result = mutableMapOf<String, Map<String, DemoSettingsEntry>>()
        for ((quest, value) in quests) {
            if (!quest.startsWith("serial:") && !quest.startsWith("ip:")) continue
            val demos = (value as? JsonObject)?.fields ?: continue
            val entries = mutableMapOf<String, DemoSettingsEntry>()
            for ((demoId, entryValue) in demos) {
                if (!DemoSwitchProtocol.isIdentifier(demoId) || DemoCatalog.sessionApps.none { it.demoId == demoId }) continue
                val f = (entryValue as? JsonObject)?.fields ?: continue
                entries[demoId] = DemoSettingsEntry(known(demoId, strings(f["hub"])), known(demoId, strings(f["unwritten"])))
            }
            if (entries.isNotEmpty()) result[quest] = entries
        }
        return result
    }

    private fun strings(value: com.hapbeat.demoremote.protocol.JsonValue?): Map<String, String> =
        (value as? JsonObject)?.fields?.mapNotNull { (k, v) -> (v as? JsonString)?.value?.let { k to it } }?.toMap() ?: emptyMap()
}
