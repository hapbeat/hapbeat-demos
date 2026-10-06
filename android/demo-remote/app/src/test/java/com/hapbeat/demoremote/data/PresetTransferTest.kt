package com.hapbeat.demoremote.data

import com.hapbeat.demoremote.data.PresetTransfer.NameConflict
import com.hapbeat.demoremote.protocol.JsonArray
import com.hapbeat.demoremote.protocol.JsonBool
import com.hapbeat.demoremote.protocol.JsonNull
import com.hapbeat.demoremote.protocol.JsonNumber
import com.hapbeat.demoremote.protocol.JsonObject
import com.hapbeat.demoremote.protocol.JsonString
import com.hapbeat.demoremote.protocol.JsonValue
import com.hapbeat.demoremote.protocol.MiniJson
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.Base64

/** demo-session.md「リモコンへのプリセット受け渡し（QR / リンク）」. */
class PresetTransferTest {
    // Copy of hapbeat-contracts fixtures/sample-demo-remote-preset.json (repos-core/hapbeat-contracts, commit 2e2561d).
    private val fixture: Map<String, JsonValue> =
        (MiniJson.parse(javaClass.getResource("/sample-demo-remote-preset.json")!!.readText(Charsets.UTF_8)) as JsonObject).fields

    /** Back to text through MiniJson.write (fixture numbers are integers). */
    private fun text(value: JsonValue): String = MiniJson.write(plain(value))

    private fun plain(value: JsonValue): Any? = when (value) {
        is JsonString -> value.value
        is JsonNumber -> value.raw.toLong()
        is JsonBool -> value.value
        JsonNull -> null
        is JsonArray -> value.items.map { plain(it) }
        is JsonObject -> LinkedHashMap(value.fields.mapValues { plain(it.value) })
    }

    private fun token(json: String): String = "v1." + Base64.getUrlEncoder().withoutPadding().encodeToString(json.toByteArray(Charsets.UTF_8))

    private fun rejected(result: TransferResult): String = (result as TransferResult.Rejected).message

    // ---- fixtures ----

    @Test
    fun fixtureValidIsAccepted() {
        val result = PresetTransfer.parsePayload(text(fixture.getValue("valid"))) as TransferResult.Accepted
        assertEquals(
            listOf(
                RemotePreset("XR Kaigi A", listOf(
                    PresetStep("energy-duel", mapOf("tutorial" to "on")),
                    PresetStep("volley", mapOf("scene" to "match"), retry = false),
                )),
                RemotePreset("短縮コース", listOf(PresetStep("handdemo"), PresetStep("trex-encounter"))),
            ),
            result.presets,
        )
        // Same through the token path.
        assertEquals(result, PresetTransfer.decodeToken(token(text(fixture.getValue("valid")))))
    }

    @Test
    fun fixtureInvalidAreAllRejected() {
        val invalid = (fixture.getValue("invalid") as JsonArray).items
        assertEquals(13, invalid.size)
        invalid.forEachIndexed { i, item ->
            val json = text(item)
            assertTrue("invalid[$i] $json", PresetTransfer.parsePayload(json) is TransferResult.Rejected)
            assertTrue("invalid[$i] token", PresetTransfer.decodeToken(token(json)) is TransferResult.Rejected)
        }
    }

    @Test
    fun fixtureValidTokenDecodesToItsJson() {
        val result = PresetTransfer.decodeToken((fixture.getValue("valid_token") as JsonString).value)
        assertEquals(PresetTransfer.parsePayload(text(fixture.getValue("valid_token_json"))), result)
        assertEquals(listOf(RemotePreset("XR Kaigi A", listOf(PresetStep("handdemo")))), (result as TransferResult.Accepted).presets)
    }

    @Test
    fun fixtureInvalidTokensAreAllRejected() {
        val tokens = (fixture.getValue("invalid_tokens") as JsonArray).items.map { (it as JsonString).value }
        assertEquals(7, tokens.size)
        tokens.forEach { assertTrue("token ${it.take(20)}", PresetTransfer.decodeToken(it) is TransferResult.Rejected) }
        assertEquals(PresetTransfer.ERROR_TOO_LARGE, rejected(PresetTransfer.decodeToken(tokens[5])))
    }

    // ---- token / payload rules ----

    @Test
    fun duplicateKeysAreRejected() {
        val json = """{"version":1,"presets":[{"name":"a","steps":[{"demo_id":"handdemo","demo_id":"fps"}]}]}"""
        assertTrue(PresetTransfer.parsePayload(json) is TransferResult.Rejected)
        assertTrue(PresetTransfer.parsePayload("""{"version":1,"version":1,"presets":[{"name":"a","steps":[{"demo_id":"fps"}]}]}""") is TransferResult.Rejected)
    }

    @Test
    fun unknownDemoIsNamed() {
        val json = """{"version":1,"presets":[{"name":"a","steps":[{"demo_id":"handdemo"},{"demo_id":"gloveball_v2"},{"demo_id":"demo_hub"}]}]}"""
        assertEquals(PresetTransfer.ERROR_UNKNOWN_DEMO + "gloveball_v2, demo_hub", rejected(PresetTransfer.parsePayload(json)))
    }

    @Test
    fun optionsMustBeInTheDescriptorTable() {
        fun payload(demo: String, options: String) = """{"version":1,"presets":[{"name":"a","steps":[{"demo_id":"$demo","options":$options}]}]}"""
        assertTrue(PresetTransfer.parsePayload(payload("volley", """{"scene":"receive","balls":"20"}""")) is TransferResult.Accepted)
        assertTrue(PresetTransfer.parsePayload(payload("volley", """{"scene":"tutorial"}""")) is TransferResult.Rejected)
        assertTrue(PresetTransfer.parsePayload(payload("volley", """{"tutorial":"on"}""")) is TransferResult.Rejected)
        assertTrue(PresetTransfer.parsePayload(payload("handdemo", """{"tutorial":"on"}""")) is TransferResult.Rejected)
        assertTrue(PresetTransfer.parsePayload(payload("boxing", """{"round":60}""")) is TransferResult.Rejected)
        assertTrue(PresetTransfer.parsePayload(payload("boxing", "{}")) is TransferResult.Accepted)
    }

    @Test
    fun countsAndTypesFollowTheSchema() {
        val step = """{"demo_id":"fps"}"""
        val preset = """{"name":"a","steps":[$step]}"""
        assertTrue(PresetTransfer.parsePayload("""{"version":1,"presets":[$preset,$preset,$preset]}""") is TransferResult.Accepted)
        assertTrue(PresetTransfer.parsePayload("""{"version":1,"presets":[$preset,$preset,$preset,$preset]}""") is TransferResult.Rejected)
        assertTrue(PresetTransfer.parsePayload("""{"version":1,"presets":[{"name":"a","steps":[${List(32) { step }.joinToString(",")}]}]}""") is TransferResult.Accepted)
        assertTrue(PresetTransfer.parsePayload("""{"version":1,"presets":[{"name":"a","steps":[${List(33) { step }.joinToString(",")}]}]}""") is TransferResult.Rejected)
        assertTrue(PresetTransfer.parsePayload("""{"version":"1","presets":[$preset]}""") is TransferResult.Rejected)
        assertTrue(PresetTransfer.parsePayload("""{"version":1,"presets":[{"name":"a","steps":[{"demo_id":"fps","retry":"no"}]}]}""") is TransferResult.Rejected)
        assertTrue(PresetTransfer.parsePayload("""{"version":1,"presets":[{"name":1,"steps":[$step]}]}""") is TransferResult.Rejected)
        assertTrue(PresetTransfer.parsePayload("""{"presets":[$preset]}""") is TransferResult.Rejected)
        assertTrue(PresetTransfer.parsePayload("""[$preset]""") is TransferResult.Rejected)
    }

    @Test
    fun nameRules() {
        assertTrue(PresetTransfer.isValidName("あ".repeat(40)))
        assertTrue(PresetTransfer.isValidName("😀".repeat(40))) // 40 code points, 80 UTF-16 chars
        assertTrue(!PresetTransfer.isValidName("a".repeat(41)))
        assertTrue(!PresetTransfer.isValidName("　")) // ideographic space only
        assertTrue(!PresetTransfer.isValidName("  "))
        assertTrue(!PresetTransfer.isValidName("a b"))
        assertTrue(!PresetTransfer.isValidName("a\u007f"))
        assertTrue(!PresetTransfer.isValidName("a\u009f"))
        assertTrue(PresetTransfer.isValidName(" a "))
    }

    @Test
    fun payloadOver700BytesIsTooLarge() {
        val name = "あ".repeat(40) // 120 bytes
        val presets = List(3) { """{"name":"$name","steps":[${List(4) { """{"demo_id":"trex-encounter"}""" }.joinToString(",")}]}""" }
        val json = """{"version":1,"presets":[${presets.joinToString(",")}]}"""
        assertTrue(json.toByteArray().size > 700)
        assertEquals(PresetTransfer.ERROR_TOO_LARGE, rejected(PresetTransfer.decodeToken(token(json))))
    }

    @Test
    fun tokenChecks() {
        val good = token("""{"version":1,"presets":[{"name":"a","steps":[{"demo_id":"fps"}]}]}""")
        assertTrue(PresetTransfer.decodeToken(good) is TransferResult.Accepted)
        // "a" is "YQ"; "YR" sets unused trailing bits (non-canonical) and must not decode to "a".
        assertEquals(PresetTransfer.ERROR_FORMAT, rejected(PresetTransfer.decodeToken("v1.YR")))
        assertTrue(PresetTransfer.decodeToken("v1.YQ") is TransferResult.Rejected) // "a": not JSON
        assertTrue(PresetTransfer.decodeToken("v1.Y") is TransferResult.Rejected) // impossible length
        assertTrue(PresetTransfer.decodeToken(" $good") is TransferResult.Rejected)
        assertTrue(PresetTransfer.decodeToken("V1." + good.substring(3)) is TransferResult.Rejected)
        // Overlong UTF-8 for '"' (0xC0 0xA2) must not be decoded leniently.
        val overlong = "v1." + Base64.getUrlEncoder().withoutPadding().encodeToString(byteArrayOf(0xC0.toByte(), 0xA2.toByte()))
        assertEquals(PresetTransfer.ERROR_FORMAT, rejected(PresetTransfer.decodeToken(overlong)))
    }

    // ---- entry points ----

    @Test
    fun qrAcceptsOnlyTheShowcaseLink() {
        assertEquals("v1.abc", PresetTransfer.tokenFromQr("https://devtools.hapbeat.com/remote/preset#v1.abc"))
        assertNull(PresetTransfer.tokenFromQr("http://devtools.hapbeat.com/remote/preset#v1.abc"))
        assertNull(PresetTransfer.tokenFromQr("https://devtools.hapbeat.com.evil.example/remote/preset#v1.abc"))
        assertNull(PresetTransfer.tokenFromQr("https://devtools.hapbeat.com/remote/preset?d=v1.abc"))
        assertNull(PresetTransfer.tokenFromQr("v1.abc"))
    }

    @Test
    fun linkAcceptsOnlyThePresetUri() {
        assertEquals("v1.abc_-", PresetTransfer.tokenFromLink("hapbeat-remote://preset?d=v1.abc_-"))
        assertEquals("v1.abc", PresetTransfer.tokenFromLink("hapbeat-remote://preset?x=1&d=v1.abc"))
        assertNull(PresetTransfer.tokenFromLink("hapbeat-remote://other?d=v1.abc"))
        assertNull(PresetTransfer.tokenFromLink("hapbeat-remote://preset/x?d=v1.abc"))
        assertNull(PresetTransfer.tokenFromLink("hapbeat-remote://user@preset?d=v1.abc"))
        assertNull(PresetTransfer.tokenFromLink("hapbeat-remote://preset:1?d=v1.abc"))
        assertNull(PresetTransfer.tokenFromLink("hapbeat-remote://preset?d=v1.a&d=v1.b"))
        assertNull(PresetTransfer.tokenFromLink("hapbeat-remote://preset"))
        assertNull(PresetTransfer.tokenFromLink("https://preset?d=v1.abc"))
        // Percent-encoding is passed through raw and then fails the token check.
        assertTrue(PresetTransfer.decodeToken(PresetTransfer.tokenFromLink("hapbeat-remote://preset?d=v1.ab%3D")!!) is TransferResult.Rejected)
    }

    // ---- merge ----

    @Test
    fun mergeOverwritesOrRenames() {
        val a = RemotePreset("A", listOf(PresetStep("fps")))
        val b = RemotePreset("B", listOf(PresetStep("fps")))
        val newA = RemotePreset("A", listOf(PresetStep("handdemo")))
        val newC = RemotePreset("C", listOf(PresetStep("boxing")))
        assertEquals(listOf(newA, b, newC), PresetTransfer.merge(listOf(a, b), listOf(newA, newC), listOf(NameConflict.OVERWRITE, NameConflict.ADD_RENAMED)))
        assertEquals(
            listOf(a, b, newA.copy(name = "A (2)"), newA.copy(name = "A (3)")),
            PresetTransfer.merge(listOf(a, b), listOf(newA, newA), listOf(NameConflict.ADD_RENAMED, NameConflict.ADD_RENAMED)),
        )
    }

    @Test
    fun uniqueNameStaysWithin40CodePoints() {
        val long = "あ".repeat(40)
        val renamed = PresetTransfer.uniqueName(long, setOf(long))
        assertEquals("あ".repeat(36) + " (2)", renamed)
        assertTrue(PresetTransfer.isValidName(renamed))
        assertEquals("x (3)", PresetTransfer.uniqueName("x", setOf("x", "x (2)")))
    }
}
