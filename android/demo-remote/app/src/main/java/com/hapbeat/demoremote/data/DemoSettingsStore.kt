package com.hapbeat.demoremote.data

import com.hapbeat.demoremote.DemoCatalog
import com.hapbeat.demoremote.protocol.DemoSwitchProtocol
import com.hapbeat.demoremote.protocol.JsonException
import com.hapbeat.demoremote.protocol.JsonNumber
import com.hapbeat.demoremote.protocol.JsonObject
import com.hapbeat.demoremote.protocol.JsonString
import com.hapbeat.demoremote.protocol.JsonValue
import com.hapbeat.demoremote.protocol.MiniJson

/**
 * One demo's per-demo settings (demo-session.md「デモごとの設定」) as the phone keeps them for one Hub: [hub] the values last
 * read from or written to that Hub (DEMO_SETTINGS), [unwritten] only the options the user changed on the phone that the
 * Hub does not have yet (the Hub was not in front or did not answer). They are written when the Hub is in front again and
 * the user asks (DEMO_SETTINGS_SET carries only these, so values changed on the headset are never overwritten).
 */
data class DemoSettingsEntry(val hub: Map<String, String> = emptyMap(), val unwritten: Map<String, String> = emptyMap()) {
    /** What the phone shows: the Hub's values with the unwritten changes over them. */
    val values: Map<String, String> get() = hub + unwritten
}

/** Unwritten changes of one demo on one Hub ([hubId]: its DEMO_SETTINGS `hub_id`). */
data class UnwrittenDemoSettings(val hubId: String, val demoId: String, val settings: Map<String, String>)

/**
 * The phone's copy of each Hub's per-demo settings: `hub_id` (DEMO_SETTINGS, the Hub's own ID, so a copy follows the
 * headset and not its IP or adb serial) → demo ID → entry. Kept in the settings (one JSON string), so unwritten changes
 * survive closing the app. A copy exists only for a Hub whose settings were read at least once. Pure Kotlin so it runs in
 * JVM unit tests. Only demos and options of this app's table with their listed values are kept; anything else read back
 * is dropped.
 */
object DemoSettingsStore {
    private const val VERSION = 2
    private val HUB_ID = Regex("^[0-9a-f]{16}$")

    fun isHubId(value: String): Boolean = HUB_ID.matches(value)

    fun entry(book: Map<String, Map<String, DemoSettingsEntry>>, hubId: String, demoId: String): DemoSettingsEntry? = book[hubId]?.get(demoId)

    /**
     * DEMO_SETTINGS of [demoId] read from the Hub [hubId]: [settings] becomes the Hub's values (only the options of this
     * app's table); unwritten changes stay, except those the Hub already has.
     */
    fun onRead(
        book: Map<String, Map<String, DemoSettingsEntry>>, hubId: String, demoId: String, settings: Map<String, String>,
    ): Map<String, Map<String, DemoSettingsEntry>> {
        val hub = known(demoId, settings)
        val old = entry(book, hubId, demoId) ?: DemoSettingsEntry()
        return put(book, hubId, demoId, DemoSettingsEntry(hub, old.unwritten.filter { (id, value) -> hub[id] != value }))
    }

    /**
     * 保存: [changed] holds only the options the user changed on the screen. They join the unwritten changes; one set back
     * to the Hub's value is no longer a change. Nothing for a Hub whose settings were never read ([entry] null).
     */
    fun save(
        book: Map<String, Map<String, DemoSettingsEntry>>, hubId: String, demoId: String, changed: Map<String, String>,
    ): Map<String, Map<String, DemoSettingsEntry>> {
        val old = entry(book, hubId, demoId) ?: return book
        val unwritten = (old.unwritten + known(demoId, changed)).filter { (id, value) -> old.hub[id] != value }
        return put(book, hubId, demoId, old.copy(unwritten = unwritten))
    }

    /** The Hub took [written] (READY): they are the Hub's values now and no longer unwritten (later changes stay). */
    fun onWritten(
        book: Map<String, Map<String, DemoSettingsEntry>>, hubId: String, demoId: String, written: Map<String, String>,
    ): Map<String, Map<String, DemoSettingsEntry>> {
        val old = entry(book, hubId, demoId) ?: return book
        return put(book, hubId, demoId, DemoSettingsEntry(old.hub + written, old.unwritten.filter { (id, value) -> written[id] != value }))
    }

    /** Every unwritten change for the Hub [hubId], in catalog order. */
    fun unwritten(book: Map<String, Map<String, DemoSettingsEntry>>, hubId: String): List<UnwrittenDemoSettings> =
        DemoCatalog.sessionApps.mapNotNull { app ->
            val changes = book[hubId]?.get(app.demoId)?.unwritten.orEmpty()
            if (changes.isEmpty()) null else UnwrittenDemoSettings(hubId, app.demoId, changes)
        }

    private fun put(
        book: Map<String, Map<String, DemoSettingsEntry>>, hubId: String, demoId: String, entry: DemoSettingsEntry,
    ): Map<String, Map<String, DemoSettingsEntry>> = book + (hubId to ((book[hubId] ?: emptyMap()) + (demoId to entry)))

    /** Only options of [demoId]'s table with one of their values. */
    private fun known(demoId: String, values: Map<String, String>): Map<String, String> {
        val table = DemoCatalog.optionsFor(demoId)
        return values.filter { (id, value) -> table.any { o -> o.id == id && o.values.any { it.value == value } } }
    }

    fun encode(book: Map<String, Map<String, DemoSettingsEntry>>): String = MiniJson.write(linkedMapOf(
        "version" to VERSION,
        "hubs" to book.toSortedMap().mapValues { (_, demos) ->
            demos.toSortedMap().mapValues { (_, entry) ->
                linkedMapOf("hub" to entry.hub.toSortedMap(), "unwritten" to entry.unwritten.toSortedMap())
            }
        },
    ))

    /** Stored copies; anything from another version (the serial / IP keyed d4 build) reads as none. */
    fun decode(text: String): Map<String, Map<String, DemoSettingsEntry>> {
        if (text.isEmpty()) return emptyMap()
        val root = try { MiniJson.parse(text) } catch (_: JsonException) { return emptyMap() }
        val fields = (root as? JsonObject)?.fields ?: return emptyMap()
        if ((fields["version"] as? JsonNumber)?.longOrNull() != VERSION.toLong()) return emptyMap()
        val hubs = (fields["hubs"] as? JsonObject)?.fields ?: return emptyMap()
        val result = mutableMapOf<String, Map<String, DemoSettingsEntry>>()
        for ((hubId, value) in hubs) {
            if (!isHubId(hubId)) continue
            val demos = (value as? JsonObject)?.fields ?: continue
            val entries = mutableMapOf<String, DemoSettingsEntry>()
            for ((demoId, entryValue) in demos) {
                if (!DemoSwitchProtocol.isIdentifier(demoId) || DemoCatalog.sessionApps.none { it.demoId == demoId }) continue
                val f = (entryValue as? JsonObject)?.fields ?: continue
                entries[demoId] = DemoSettingsEntry(known(demoId, strings(f["hub"])), known(demoId, strings(f["unwritten"])))
            }
            if (entries.isNotEmpty()) result[hubId] = entries
        }
        return result
    }

    private fun strings(value: JsonValue?): Map<String, String> =
        (value as? JsonObject)?.fields?.mapNotNull { (k, v) -> (v as? JsonString)?.value?.let { k to it } }?.toMap() ?: emptyMap()
}
