package com.hapbeat.demoremote.protocol

/**
 * Minimal strict JSON reader/writer for Demo Switch payloads and local settings.
 * Pure Kotlin so it runs in JVM unit tests (org.json is a stub there).
 * Duplicate object keys are rejected; numbers keep their raw text so integers
 * up to 2^53-1 are compared exactly.
 */
sealed interface JsonValue

data class JsonString(val value: String) : JsonValue
data class JsonNumber(val raw: String) : JsonValue {
    /** Integer without fraction/exponent and without leading zeros (RFC 8259 grammar already forbids them). */
    val isInteger: Boolean get() = INTEGER.matches(raw)
    fun longOrNull(): Long? = if (isInteger) raw.toLongOrNull() else null

    private companion object {
        val INTEGER = Regex("-?(0|[1-9][0-9]*)")
    }
}
data class JsonBool(val value: Boolean) : JsonValue
data object JsonNull : JsonValue
data class JsonArray(val items: List<JsonValue>) : JsonValue
data class JsonObject(val fields: Map<String, JsonValue>) : JsonValue

class JsonException(message: String) : Exception(message)

object MiniJson {
    private const val MAX_DEPTH = 32

    fun parse(text: String): JsonValue {
        val parser = Parser(text)
        parser.skipWhitespace()
        val value = parser.readValue(0)
        parser.skipWhitespace()
        if (!parser.atEnd()) throw JsonException("trailing data")
        return value
    }

    /** Serializes ordered fields. Values may be String, Int, Long, Boolean, null, List or Map. */
    fun write(value: Any?): String = StringBuilder().also { append(it, value) }.toString()

    private fun append(out: StringBuilder, value: Any?) {
        when (value) {
            null -> out.append("null")
            is String -> quote(out, value)
            is Int, is Long -> out.append(value.toString())
            is Boolean -> out.append(value.toString())
            is List<*> -> {
                out.append('[')
                value.forEachIndexed { i, item -> if (i > 0) out.append(','); append(out, item) }
                out.append(']')
            }
            is Map<*, *> -> {
                out.append('{')
                var first = true
                for ((k, v) in value) {
                    if (!first) out.append(',')
                    first = false
                    quote(out, k as String)
                    out.append(':')
                    append(out, v)
                }
                out.append('}')
            }
            else -> throw IllegalArgumentException("unsupported JSON value type")
        }
    }

    private fun quote(out: StringBuilder, s: String) {
        out.append('"')
        for (c in s) {
            when (c) {
                '"' -> out.append("\\\"")
                '\\' -> out.append("\\\\")
                '\n' -> out.append("\\n")
                '\r' -> out.append("\\r")
                '\t' -> out.append("\\t")
                '\b' -> out.append("\\b")
                '\u000c' -> out.append("\\f")
                else -> if (c < ' ') out.append("\\u%04x".format(c.code)) else out.append(c)
            }
        }
        out.append('"')
    }

    private class Parser(private val s: String) {
        private var i = 0

        fun atEnd() = i >= s.length

        fun skipWhitespace() {
            while (i < s.length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++
        }

        fun readValue(depth: Int): JsonValue {
            if (depth > MAX_DEPTH) throw JsonException("too deep")
            if (atEnd()) throw JsonException("unexpected end")
            return when (val c = s[i]) {
                '{' -> readObject(depth)
                '[' -> readArray(depth)
                '"' -> JsonString(readString())
                't' -> literal("true", JsonBool(true))
                'f' -> literal("false", JsonBool(false))
                'n' -> literal("null", JsonNull)
                else -> if (c == '-' || c in '0'..'9') readNumber() else throw JsonException("unexpected character")
            }
        }

        private fun literal(word: String, value: JsonValue): JsonValue {
            if (!s.startsWith(word, i)) throw JsonException("invalid literal")
            i += word.length
            return value
        }

        private fun readObject(depth: Int): JsonObject {
            i++ // {
            val fields = LinkedHashMap<String, JsonValue>()
            skipWhitespace()
            if (peek() == '}') { i++; return JsonObject(fields) }
            while (true) {
                skipWhitespace()
                if (peek() != '"') throw JsonException("expected key")
                val key = readString()
                skipWhitespace()
                if (peek() != ':') throw JsonException("expected colon")
                i++
                skipWhitespace()
                val value = readValue(depth + 1)
                if (fields.put(key, value) != null) throw JsonException("duplicate key")
                skipWhitespace()
                when (peek()) {
                    ',' -> i++
                    '}' -> { i++; return JsonObject(fields) }
                    else -> throw JsonException("expected , or }")
                }
            }
        }

        private fun readArray(depth: Int): JsonArray {
            i++ // [
            val items = ArrayList<JsonValue>()
            skipWhitespace()
            if (peek() == ']') { i++; return JsonArray(items) }
            while (true) {
                skipWhitespace()
                items.add(readValue(depth + 1))
                skipWhitespace()
                when (peek()) {
                    ',' -> i++
                    ']' -> { i++; return JsonArray(items) }
                    else -> throw JsonException("expected , or ]")
                }
            }
        }

        private fun readString(): String {
            i++ // opening quote
            val sb = StringBuilder()
            while (true) {
                if (atEnd()) throw JsonException("unterminated string")
                val c = s[i++]
                when {
                    c == '"' -> return sb.toString()
                    c == '\\' -> {
                        if (atEnd()) throw JsonException("bad escape")
                        when (val e = s[i++]) {
                            '"' -> sb.append('"')
                            '\\' -> sb.append('\\')
                            '/' -> sb.append('/')
                            'b' -> sb.append('\b')
                            'f' -> sb.append('\u000c')
                            'n' -> sb.append('\n')
                            'r' -> sb.append('\r')
                            't' -> sb.append('\t')
                            'u' -> {
                                if (i + 4 > s.length) throw JsonException("bad unicode escape")
                                val hex = s.substring(i, i + 4)
                                if (!hex.all { it in '0'..'9' || it in 'a'..'f' || it in 'A'..'F' }) throw JsonException("bad unicode escape")
                                sb.append(hex.toInt(16).toChar())
                                i += 4
                            }
                            else -> throw JsonException("bad escape $e")
                        }
                    }
                    c < ' ' -> throw JsonException("control character in string")
                    else -> sb.append(c)
                }
            }
        }

        private fun readNumber(): JsonNumber {
            val start = i
            if (peek() == '-') i++
            if (peek() == '0') i++ else readDigits(required = true)
            if (peek() == '.') { i++; readDigits(required = true) }
            if (peek() == 'e' || peek() == 'E') {
                i++
                if (peek() == '+' || peek() == '-') i++
                readDigits(required = true)
            }
            return JsonNumber(s.substring(start, i))
        }

        private fun readDigits(required: Boolean) {
            val start = i
            while (i < s.length && s[i] in '0'..'9') i++
            if (required && i == start) throw JsonException("expected digit")
        }

        private fun peek(): Char = if (i < s.length) s[i] else '\u0000'
    }
}
