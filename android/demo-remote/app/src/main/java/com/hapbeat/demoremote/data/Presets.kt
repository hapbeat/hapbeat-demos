package com.hapbeat.demoremote.data

import com.hapbeat.demoremote.protocol.JsonArray
import com.hapbeat.demoremote.protocol.JsonException
import com.hapbeat.demoremote.protocol.JsonObject
import com.hapbeat.demoremote.protocol.JsonString
import com.hapbeat.demoremote.protocol.MiniJson

/** One demo in a remote preset. [tutorial]: "on" / "off", or null for the demo's descriptor default. */
data class PresetStep(val demoId: String, val tutorial: String? = null)

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
                linkedMapOf<String, Any?>("demo_id" to step.demoId).apply { if (step.tutorial != null) put("tutorial", step.tutorial) }
            },
        )
    })

    /** Unreadable entries are dropped rather than failing the whole list. */
    fun decode(raw: String?): List<RemotePreset> {
        if (raw.isNullOrEmpty()) return emptyList()
        val root = try { MiniJson.parse(raw) } catch (_: JsonException) { return emptyList() }
        return (root as? JsonArray)?.items.orEmpty().mapNotNull { item ->
            val f = (item as? JsonObject)?.fields ?: return@mapNotNull null
            val name = (f["name"] as? JsonString)?.value ?: return@mapNotNull null
            val steps = (f["steps"] as? JsonArray)?.items.orEmpty().mapNotNull { s ->
                val sf = (s as? JsonObject)?.fields ?: return@mapNotNull null
                val demoId = (sf["demo_id"] as? JsonString)?.value ?: return@mapNotNull null
                PresetStep(demoId, (sf["tutorial"] as? JsonString)?.value?.takeIf { it == "on" || it == "off" })
            }
            RemotePreset(name, steps)
        }
    }
}
