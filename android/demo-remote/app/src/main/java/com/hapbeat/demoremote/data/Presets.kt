package com.hapbeat.demoremote.data

import com.hapbeat.demoremote.protocol.JsonArray
import com.hapbeat.demoremote.protocol.JsonBool
import com.hapbeat.demoremote.protocol.JsonException
import com.hapbeat.demoremote.protocol.JsonObject
import com.hapbeat.demoremote.protocol.JsonString
import com.hapbeat.demoremote.protocol.MiniJson

/**
 * One demo in a remote preset, same shape as a step of the Hub start extra (demo-session.md):
 * [options] descriptor option id → value (missing = the demo's default), [retry] false = no retry on failure.
 */
data class PresetStep(val demoId: String, val options: Map<String, String> = emptyMap(), val retry: Boolean = true)

/**
 * A session plan kept on this phone and started through the Hub's external start extra (`steps`).
 * Independent of the Hub's own presets 1..3, which stay edited inside VR.
 */
data class RemotePreset(val name: String, val steps: List<PresetStep>)

/** Storage format of the remote presets (SharedPreferences string). */
object PresetCodec {
    fun encode(presets: List<RemotePreset>): String = MiniJson.write(presets.map { preset ->
        linkedMapOf(
            "name" to preset.name,
            "steps" to preset.steps.map { step ->
                linkedMapOf<String, Any?>("demo_id" to step.demoId).apply {
                    if (step.options.isNotEmpty()) put("options", LinkedHashMap(step.options))
                    put("retry", step.retry)
                }
            },
        )
    })

    /** Unreadable entries are dropped rather than failing the whole list (no migration of older formats). */
    fun decode(raw: String?): List<RemotePreset> {
        if (raw.isNullOrEmpty()) return emptyList()
        val root = try { MiniJson.parse(raw) } catch (_: JsonException) { return emptyList() }
        return (root as? JsonArray)?.items.orEmpty().mapNotNull { item ->
            val f = (item as? JsonObject)?.fields ?: return@mapNotNull null
            val name = (f["name"] as? JsonString)?.value ?: return@mapNotNull null
            val steps = (f["steps"] as? JsonArray)?.items.orEmpty().mapNotNull { s -> decodeStep(s) }
            if (steps.isEmpty()) null else RemotePreset(name, steps)
        }
    }

    private fun decodeStep(value: Any): PresetStep? {
        val f = (value as? JsonObject)?.fields ?: return null
        if (!f.keys.all { it == "demo_id" || it == "options" || it == "retry" }) return null
        val demoId = (f["demo_id"] as? JsonString)?.value ?: return null
        val options = when (val o = f["options"]) {
            null -> emptyMap()
            is JsonObject -> o.fields.mapValues { (it.value as? JsonString)?.value ?: return null }
            else -> return null
        }
        val retry = when (val r = f["retry"]) {
            null -> true
            is JsonBool -> r.value
            else -> return null
        }
        return PresetStep(demoId, options, retry)
    }
}
