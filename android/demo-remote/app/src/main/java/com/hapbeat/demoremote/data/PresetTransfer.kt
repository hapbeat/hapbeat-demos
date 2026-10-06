package com.hapbeat.demoremote.data

import com.hapbeat.demoremote.DemoCatalog
import com.hapbeat.demoremote.protocol.JsonArray
import com.hapbeat.demoremote.protocol.JsonBool
import com.hapbeat.demoremote.protocol.JsonException
import com.hapbeat.demoremote.protocol.JsonNumber
import com.hapbeat.demoremote.protocol.JsonObject
import com.hapbeat.demoremote.protocol.JsonString
import com.hapbeat.demoremote.protocol.JsonValue
import com.hapbeat.demoremote.protocol.MiniJson
import java.math.BigDecimal
import java.net.URI
import java.net.URISyntaxException
import java.nio.ByteBuffer
import java.nio.charset.CharacterCodingException
import java.nio.charset.CodingErrorAction
import java.util.Base64

/** Outcome of reading a preset transfer token. [Rejected.message] is shown to the user as-is. */
sealed interface TransferResult {
    data class Accepted(val presets: List<RemotePreset>) : TransferResult
    data class Rejected(val message: String) : TransferResult
}

/**
 * Presets handed over from the web showcase by QR or link (demo-session.md「リモコンへのプリセット受け渡し（QR /
 * リンク）」, demo-remote-preset.schema.json). Pure Kotlin so the whole check runs in JVM unit tests.
 * Anything that breaks a rule rejects the whole payload; nothing is imported partially.
 */
object PresetTransfer {
    const val WEB_PREFIX = "https://devtools.hapbeat.com/remote/preset#"
    const val LINK_SCHEME = "hapbeat-remote"
    const val LINK_HOST = "preset"
    const val MAX_TOKEN_CHARS = 937
    const val MAX_PAYLOAD_BYTES = 700
    const val MAX_PRESETS = 3
    const val MAX_STEPS = 32
    const val MAX_NAME_CODE_POINTS = 40

    const val ERROR_FORMAT = "QR・リンクの形式が違います"
    const val ERROR_TOO_LARGE = "データが大きすぎます"
    const val ERROR_CONTENT = "プリセットの内容が正しくありません"
    const val ERROR_UNKNOWN_DEMO = "このデモはリモコンに登録されていません: "

    private val TOKEN = Regex("^v1\\.[A-Za-z0-9_-]+$")

    /** Token from an in-app QR scan: the part after `#` of the showcase link, or null for any other text. */
    fun tokenFromQr(text: String): String? = if (text.startsWith(WEB_PREFIX)) text.substring(WEB_PREFIX.length) else null

    /**
     * Token from a `hapbeat-remote://preset?d=<token>` link, or null for any other URI (wrong scheme / host, a path,
     * user info, port, fragment, or not exactly one `d`). The raw (undecoded) query value is used: a valid token
     * needs no percent-encoding, so an encoded one fails the token check.
     */
    fun tokenFromLink(uri: String): String? {
        val parsed = try { URI(uri) } catch (_: URISyntaxException) { return null }
        if (parsed.scheme != LINK_SCHEME || parsed.rawAuthority != LINK_HOST || parsed.host != LINK_HOST) return null
        if (!parsed.rawPath.isNullOrEmpty() || parsed.rawFragment != null) return null
        val values = parsed.rawQuery.orEmpty().split('&').filter { it.startsWith("d=") }.map { it.substring(2) }
        return values.singleOrNull()
    }

    /** Checks and decodes [token] (`v1.` + base64url without padding of UTF-8 JSON), then validates the payload. */
    fun decodeToken(token: String): TransferResult {
        if (token.length > MAX_TOKEN_CHARS) return TransferResult.Rejected(ERROR_TOO_LARGE)
        if (!TOKEN.matches(token)) return TransferResult.Rejected(ERROR_FORMAT)
        val encoded = token.substring(3)
        val bytes = try { Base64.getUrlDecoder().decode(encoded) } catch (_: IllegalArgumentException) {
            return TransferResult.Rejected(ERROR_FORMAT)
        }
        // Strict: only the canonical encoding (no stray trailing bits) is accepted.
        if (Base64.getUrlEncoder().withoutPadding().encodeToString(bytes) != encoded) return TransferResult.Rejected(ERROR_FORMAT)
        if (bytes.size > MAX_PAYLOAD_BYTES) return TransferResult.Rejected(ERROR_TOO_LARGE)
        val text = try {
            Charsets.UTF_8.newDecoder()
                .onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT)
                .decode(ByteBuffer.wrap(bytes))
                .toString()
        } catch (_: CharacterCodingException) {
            return TransferResult.Rejected(ERROR_FORMAT)
        }
        return parsePayload(text)
    }

    /** Validates the payload JSON (duplicate keys are rejected by [MiniJson]). */
    fun parsePayload(text: String): TransferResult {
        if (text.toByteArray(Charsets.UTF_8).size > MAX_PAYLOAD_BYTES) return TransferResult.Rejected(ERROR_TOO_LARGE)
        val root = try { MiniJson.parse(text) } catch (_: JsonException) { return TransferResult.Rejected(ERROR_CONTENT) }
        val presets = readRoot(root) ?: return TransferResult.Rejected(ERROR_CONTENT)
        // Schema passed: now the device allow list (Hub-startable demos) and each demo's descriptor options.
        val unknown = presets.flatMap { p -> p.steps.map { it.demoId } }.filter { id -> DemoCatalog.sessionApps.none { it.demoId == id } }
        if (unknown.isNotEmpty()) return TransferResult.Rejected(ERROR_UNKNOWN_DEMO + unknown.distinct().joinToString(", "))
        for (step in presets.flatMap { it.steps }) {
            val table = DemoCatalog.optionsFor(step.demoId)
            val bad = step.options.entries.firstOrNull { (key, value) -> table.none { o -> o.id == key && o.values.any { it.value == value } } }
            if (bad != null) {
                return TransferResult.Rejected("${DemoCatalog.labelFor(step.demoId)} にない設定です: ${bad.key}=${bad.value}")
            }
        }
        return TransferResult.Accepted(presets)
    }

    // ---- schema (demo-remote-preset.schema.json); null = invalid ----

    private fun readRoot(value: JsonValue): List<RemotePreset>? {
        val f = (value as? JsonObject)?.fields ?: return null
        if (f.keys != setOf("version", "presets")) return null
        val version = f["version"] as? JsonNumber ?: return null
        if (version.raw.toBigDecimalOrNull()?.compareTo(BigDecimal.ONE) != 0) return null
        val items = (f["presets"] as? JsonArray)?.items ?: return null
        if (items.size !in 1..MAX_PRESETS) return null
        return items.map { readPreset(it) ?: return null }
    }

    private fun readPreset(value: JsonValue): RemotePreset? {
        val f = (value as? JsonObject)?.fields ?: return null
        if (f.keys != setOf("name", "steps")) return null
        val name = (f["name"] as? JsonString)?.value ?: return null
        if (!isValidName(name)) return null
        val items = (f["steps"] as? JsonArray)?.items ?: return null
        if (items.size !in 1..MAX_STEPS) return null
        return RemotePreset(name, items.map { readStep(it) ?: return null })
    }

    private fun readStep(value: JsonValue): PresetStep? {
        val f = (value as? JsonObject)?.fields ?: return null
        if (!f.keys.all { it == "demo_id" || it == "options" || it == "retry" }) return null
        val demoId = (f["demo_id"] as? JsonString)?.value ?: return null
        if (!DemoCatalog.IDENTIFIER.matches(demoId)) return null
        val options = when (val o = f["options"]) {
            null -> emptyMap()
            is JsonObject -> {
                if (o.fields.size > DemoCatalog.MAX_OPTIONS) return null
                o.fields.mapValues { (key, v) ->
                    val text = (v as? JsonString)?.value ?: return null
                    if (!DemoCatalog.IDENTIFIER.matches(key) || !DemoCatalog.OPTION_VALUE.matches(text)) return null
                    text
                }
            }
            else -> return null
        }
        val retry = when (val r = f["retry"]) {
            null -> true
            is JsonBool -> r.value
            else -> return null
        }
        return PresetStep(demoId, options, retry)
    }

    /**
     * 1..40 code points, at least one non-whitespace character (ECMAScript `\S`, as the schema pattern), and no
     * C0 / C1 control characters or U+2028 / U+2029.
     */
    fun isValidName(name: String): Boolean {
        if (name.codePointCount(0, name.length) !in 1..MAX_NAME_CODE_POINTS) return false
        if (name.any { it in '\u0000'..'\u001f' || it in '\u007f'..'\u009f' || it == ' ' || it == ' ' }) return false
        return name.any { !isEcmaWhitespace(it) }
    }

    /** ECMAScript WhiteSpace + LineTerminator (what `\s` matches in a JSON Schema pattern). */
    private fun isEcmaWhitespace(c: Char): Boolean =
        c == '\t' || c == '\u000b' || c == '\u000c' || c == ' ' || c == ' ' || c == '﻿' ||
            c == '\n' || c == '\r' || c == ' ' || c == ' ' || Character.getType(c) == Character.SPACE_SEPARATOR.toInt()

    // ---- merging into the phone's list ----

    /** How one imported preset is stored when the name is already taken. */
    enum class NameConflict { OVERWRITE, ADD_RENAMED }

    /**
     * [existing] with [imported] added. A preset whose name is taken is either written over the first preset of
     * that name ([NameConflict.OVERWRITE]) or added as "name (2)", "name (3)", ... ([NameConflict.ADD_RENAMED]).
     * [choices] is parallel to [imported]; missing entries add renamed.
     */
    fun merge(existing: List<RemotePreset>, imported: List<RemotePreset>, choices: List<NameConflict>): List<RemotePreset> {
        val result = existing.toMutableList()
        imported.forEachIndexed { i, preset ->
            val at = result.indexOfFirst { it.name == preset.name }
            when {
                at < 0 -> result.add(preset)
                choices.getOrNull(i) == NameConflict.OVERWRITE -> result[at] = preset
                else -> result.add(preset.copy(name = uniqueName(preset.name, result.map { it.name }.toSet())))
            }
        }
        return result
    }

    /** "name (n)" with the smallest free n >= 2, shortening the base so the result stays within 40 code points. */
    fun uniqueName(name: String, taken: Set<String>): String {
        var n = 2
        while (true) {
            val suffix = " ($n)"
            val room = MAX_NAME_CODE_POINTS - suffix.length
            val base = if (name.codePointCount(0, name.length) <= room) name else name.substring(0, name.offsetByCodePoints(0, room))
            val candidate = base + suffix
            if (candidate !in taken) return candidate
            n++
        }
    }
}
