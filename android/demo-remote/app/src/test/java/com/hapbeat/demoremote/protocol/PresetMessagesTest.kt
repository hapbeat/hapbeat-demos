package com.hapbeat.demoremote.protocol

import com.hapbeat.demoremote.data.PresetStep
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** PRESET_GET / PRESET / PRESET_SET / PRESET_START (demo-switch-control.md「Hub presets」). */
class PresetMessagesTest {
    private val unsigned = AuthConfig(secret = null, allowUnsigned = true)
    private val signed = AuthConfig(secret = "test-secret", allowUnsigned = false)

    // hapbeat-contracts fixtures/sample-demo-switch-messages.json (tmp 324a009): unsigned_preset_*, written compact.
    private val fixtureGet = """{"version":1,"type":"PRESET_GET","controller_id":"remote-pixel","nonce":"0123456789abcdef","preset":1,"from":0}"""
    private val fixtureSteps = """[{"demo_id":"energy-duel","options":{"tutorial":"on"}},{"demo_id":"volley","options":{"scene":"match"},"retry":false}]"""
    private val fixturePreset = """{"version":1,"type":"PRESET","controller_id":"remote-pixel","nonce":"0123456789abcdef","preset":1,""" +
        """"revision":7,"name":"XR Kaigi A","visible":true,"step_count":2,"from":0,"steps":$fixtureSteps}"""
    private val fixtureSet = """{"version":1,"type":"PRESET_SET","controller_id":"remote-pixel","seq":44,"demo_id":"demo_hub","preset":1,""" +
        """"name":"XR Kaigi A","visible":true,"steps":$fixtureSteps}"""
    private val fixtureStart = """{"version":1,"type":"PRESET_START","controller_id":"remote-pixel","seq":45,"demo_id":"demo_hub","preset":1}"""

    private val steps = listOf(
        PresetStep("energy-duel", mapOf("tutorial" to "on")),
        PresetStep("volley", mapOf("scene" to "match"), retry = false),
    )

    private fun parse(json: String) = DemoSwitchProtocol.parse(json.toByteArray(Charsets.UTF_8))

    /** Closing `auth` field of a signed message (test vectors for key "test-secret", not secrets). */
    private fun withAuth(hmac: String) = ",\"" + "auth" + "\":\"" + hmac + "\"}"

    // ---- fixtures ----

    @Test
    fun fixturesParse() {
        assertEquals(DemoSwitchMessage.PresetGet("remote-pixel", "0123456789abcdef", 1, 0, null), parse(fixtureGet))
        assertEquals(
            DemoSwitchMessage.Preset("remote-pixel", "0123456789abcdef", 1, 7, "XR Kaigi A", true, 2, 0, steps, null),
            parse(fixturePreset),
        )
        assertEquals(DemoSwitchMessage.PresetSet("remote-pixel", 44, "demo_hub", 1, "XR Kaigi A", true, steps, null), parse(fixtureSet))
        assertEquals(DemoSwitchMessage.PresetStart("remote-pixel", 45, "demo_hub", 1, null), parse(fixtureStart))
    }

    @Test
    fun buildMatchesFixtures() {
        assertEquals(fixtureGet, DemoSwitchProtocol.buildPresetGet("remote-pixel", "0123456789abcdef", 1, 0, unsigned))
        assertEquals(fixtureSet, DemoSwitchProtocol.buildPresetSet("remote-pixel", 44, 1, "XR Kaigi A", true, steps, unsigned))
        assertEquals(fixtureStart, DemoSwitchProtocol.buildPresetStart("remote-pixel", 45, 1, unsigned))
    }

    @Test
    fun emptyNameAndStepsClearAPreset() {
        val json = DemoSwitchProtocol.buildPresetSet("remote-pixel", 46, 2, "", false, emptyList(), unsigned)
        assertEquals("""{"version":1,"type":"PRESET_SET","controller_id":"remote-pixel","seq":46,"demo_id":"demo_hub","preset":2,"name":"","visible":false,"steps":[]}""", json)
        assertEquals(DemoSwitchMessage.PresetSet("remote-pixel", 46, "demo_hub", 2, "", false, emptyList(), null), parse(json))
    }

    // ---- canonical bytes / HMAC (expected values computed independently with Python hmac/hashlib, key "test-secret") ----

    @Test
    fun canonicalStepsMatchesSpecExample() {
        assertEquals("energy-duel;tutorial=on;1|volley;scene=match;0", DemoSwitchProtocol.canonicalSteps(steps))
        assertTrue(
            DemoSwitchProtocol.canonicalPresetSet("remote-pixel", 44, 1, "XR Kaigi A", true, steps)
                .endsWith("steps=46:energy-duel;tutorial=on;1|volley;scene=match;0\n"),
        )
        // Options sorted by key; no options leave the middle empty.
        assertEquals(
            "energy-duel;difficulty=strong,mode=free,tutorial=off;1|handdemo;;1",
            DemoSwitchProtocol.canonicalSteps(listOf(
                PresetStep("energy-duel", linkedMapOf("tutorial" to "off", "mode" to "free", "difficulty" to "strong")),
                PresetStep("handdemo"),
            )),
        )
    }

    @Test
    fun canonicalPresetSetLayout() = assertEquals(
        "HAPBEAT-DEMO-SWITCH/1\nCOMMAND\nversion=1:1\ntype=10:PRESET_SET\ncontroller_id=12:remote-pixel\nseq=2:44\n" +
            "demo_id=8:demo_hub\npreset=1:1\nname=10:XR Kaigi A\nvisible=4:true\nsteps=46:energy-duel;tutorial=on;1|volley;scene=match;0\n",
        DemoSwitchProtocol.canonicalPresetSet("remote-pixel", 44, 1, "XR Kaigi A", true, steps),
    )

    @Test
    fun hmacPresetMessages() {
        assertEquals(
            HMAC_PRESET_GET,
            DemoSwitchProtocol.hmacHex("test-secret", DemoSwitchProtocol.canonicalPresetGet("remote-pixel", "0123456789abcdef", 1, 0)),
        )
        assertEquals(
            HMAC_PRESET,
            DemoSwitchProtocol.hmacHex("test-secret", DemoSwitchProtocol.canonical(parse(fixturePreset)!!)),
        )
        assertEquals(
            HMAC_PRESET_SET,
            DemoSwitchProtocol.hmacHex("test-secret", DemoSwitchProtocol.canonicalPresetSet("remote-pixel", 44, 1, "XR Kaigi A", true, steps)),
        )
        assertEquals(
            "0a85b77f06b1727a03eb4874ad6bbf729ca5f10aaf3951d230d364899300ef77",
            DemoSwitchProtocol.hmacHex("test-secret", DemoSwitchProtocol.canonicalPresetStart("remote-pixel", 45, 1)),
        )
        val sorted = listOf(
            PresetStep("energy-duel", linkedMapOf("tutorial" to "off", "mode" to "free", "difficulty" to "strong")),
            PresetStep("handdemo"),
        )
        assertEquals(
            "dc7be339a0ad1ec58b3bbb28b8bc22eb71822361264ca89845f584cedf1c17a6",
            DemoSwitchProtocol.hmacHex("test-secret", DemoSwitchProtocol.canonicalPresetSet("android-0a1b2c3d", 7, 3, "展示 A", false, sorted)),
        )
        assertEquals(
            "7c021ea182a175c17aef09a4c3f407fa4e417c19f13f3e7c7971b1a8c35f71ac",
            DemoSwitchProtocol.hmacHex("test-secret", DemoSwitchProtocol.canonicalPresetSet("android-0a1b2c3d", 8, 2, "", true, emptyList())),
        )
    }

    @Test
    fun signedBuildCarriesAuth() {
        val json = DemoSwitchProtocol.buildPresetSet("remote-pixel", 44, 1, "XR Kaigi A", true, steps, signed)
        assertTrue(json.endsWith(withAuth(HMAC_PRESET_SET)))
        assertTrue(DemoSwitchProtocol.buildPresetGet("remote-pixel", "0123456789abcdef", 1, 0, signed).endsWith(withAuth(HMAC_PRESET_GET)))
    }

    // ---- acceptance ----

    @Test
    fun presetAcceptance() {
        val preset = parse(fixturePreset) as DemoSwitchMessage.Preset
        val target = { nonce: String -> if (nonce == "0123456789abcdef") "192.168.0.37" else null }
        assertTrue(DemoSwitchProtocol.acceptPreset(preset, "192.168.0.37", "remote-pixel", target, unsigned))
        assertFalse(DemoSwitchProtocol.acceptPreset(preset, "192.168.0.38", "remote-pixel", target, unsigned))
        assertFalse(DemoSwitchProtocol.acceptPreset(preset, "192.168.0.37", "other", target, unsigned))
        // Unsigned reply while a secret is set.
        assertFalse(DemoSwitchProtocol.acceptPreset(preset, "192.168.0.37", "remote-pixel", target, signed))
        val signedPreset = parse(
            fixturePreset.dropLast(1) + withAuth(HMAC_PRESET),
        ) as DemoSwitchMessage.Preset
        assertTrue(DemoSwitchProtocol.acceptPreset(signedPreset, "192.168.0.37", "remote-pixel", target, signed))
        // Any change to a signed field (here one option value) breaks the HMAC.
        val tampered = signedPreset.copy(steps = listOf(PresetStep("energy-duel", mapOf("tutorial" to "off")), steps[1]))
        assertFalse(DemoSwitchProtocol.acceptPreset(tampered, "192.168.0.37", "remote-pixel", target, signed))
    }

    // ---- schema strictness ----

    @Test
    fun presetRejectsSchemaViolations() {
        listOf(
            fixturePreset.replace("}]}", "}],\"extra\":0}"),                       // unknown field
            fixturePreset.replace(",\"revision\":7", ""),                          // missing field
            fixturePreset.replace("\"preset\":1", "\"preset\":4"),
            fixturePreset.replace("\"preset\":1", "\"preset\":0"),
            fixturePreset.replace("\"from\":0", "\"from\":32"),
            fixturePreset.replace("\"step_count\":2", "\"step_count\":33"),
            fixturePreset.replace("\"revision\":7", "\"revision\":-1"),
            fixturePreset.replace("\"revision\":7", "\"revision\":7.0"),
            fixturePreset.replace("\"visible\":true", "\"visible\":1"),
            fixturePreset.replace("\"name\":\"XR Kaigi A\"", "\"name\":\"   \""),     // blank name
            fixturePreset.replace("\"name\":\"XR Kaigi A\"", "\"name\":\"a\\nb\""),  // control character
            fixturePreset.replace("\"name\":\"XR Kaigi A\"", "\"name\":\"${"x".repeat(41)}\""),
            fixturePreset.replace("\"retry\":false", "\"retry\":\"false\""),
            fixturePreset.replace("\"retry\":false", "\"retry\":false,\"title\":\"x\""), // unknown step field
            fixturePreset.replace("\"demo_id\":\"volley\"", "\"demo_id\":\"Volley\""),
            fixturePreset.replace("\"scene\":\"match\"", "\"scene\":\"Match\""),
            fixturePreset.replace("\"scene\":\"match\"", "\"scene\":\"${"m".repeat(33)}\""),
            fixturePreset.replace("\"scene\":\"match\"", "\"scene\":1"),
            fixturePreset.replace("\"steps\":[", "\"steps\":{").replace("}]}", "}}}"),
            fixturePreset.replace("\"nonce\":\"0123456789abcdef\"", "\"nonce\":\"0123456789ABCDEF\""),
        ).forEach { assertNull(it, parse(it)) }
        // The name rule allows the empty string (no name).
        assertTrue(parse(fixturePreset.replace("\"name\":\"XR Kaigi A\"", "\"name\":\"\"")) is DemoSwitchMessage.Preset)
    }

    @Test
    fun stepAndOptionLimits() {
        val step = """{"demo_id":"handdemo"}"""
        val many = { n: Int -> fixtureSet.replace("\"steps\":$fixtureSteps", "\"steps\":[" + List(n) { step }.joinToString(",") + "]") }
        assertTrue(parse(many(32)) is DemoSwitchMessage.PresetSet)
        assertNull(parse(many(33)))
        val options = { n: Int -> fixtureSet.replace("{\"tutorial\":\"on\"}", "{" + (1..n).joinToString(",") { "\"k$it\":\"v\"" } + "}") }
        assertTrue(parse(options(8)) is DemoSwitchMessage.PresetSet)
        assertNull(parse(options(9)))
    }

    @Test
    fun commandsRejectSchemaViolations() {
        assertNull(parse(fixtureSet.replace("\"demo_id\":\"demo_hub\"", "\"demo_id\":\"volley\"")))
        assertNull(parse(fixtureSet.replace("\"seq\":44", "\"seq\":0")))
        assertNull(parse(fixtureSet.replace(",\"visible\":true", "")))
        assertNull(parse(fixtureSet.replace("}]}", "}],\"from\":0}")))
        assertNull(parse(fixtureStart.replace("\"demo_id\":\"demo_hub\"", "\"demo_id\":\"handdemo\"")))
        assertNull(parse(fixtureStart.replace("\"preset\":1", "\"preset\":\"1\"")))
        assertNull(parse(fixtureStart.replace("}", ",\"name\":\"\"}")))
        assertNull(parse(fixtureGet.replace("\"from\":0", "\"from\":-1")))
        assertNull(parse(fixtureGet.replace(",\"from\":0", "")))
        assertNull(parse(fixtureGet.replace("}", ",\"seq\":1}")))
    }

    @Test
    fun payloadOver1024BytesIsRejected() {
        // Valid shape but over the datagram limit: 32 steps with long option values.
        val step = """{"demo_id":"energy-duel","options":{"tutorial":"${"o".repeat(32)}"}}"""
        val big = fixtureSet.replace("\"steps\":$fixtureSteps", "\"steps\":[" + List(32) { step }.joinToString(",") + "]")
        assertTrue(big.toByteArray().size > DemoSwitchProtocol.MAX_PAYLOAD_BYTES)
        assertNull(parse(big))
    }

    @Test
    fun worstCaseBytesCountMaxSequenceAndAuth() {
        val unsignedBytes = DemoSwitchProtocol.buildPresetSet("remote-pixel", 1, 1, "XR Kaigi A", true, steps, unsigned).toByteArray().size
        val worst = DemoSwitchProtocol.presetSetWorstCaseBytes("remote-pixel", 1, "XR Kaigi A", true, steps)
        // ,"auth":"<64 hex>" is 74 bytes; seq 1 -> 9007199254740991 adds 15 digits.
        assertEquals(unsignedBytes + 74 + 15, worst)
        assertEquals(
            DemoSwitchProtocol.buildPresetSet("remote-pixel", DemoSwitchProtocol.MAX_SEQ, 1, "XR Kaigi A", true, steps, signed).toByteArray().size,
            worst,
        )
    }

    private companion object {
        // Expected values computed independently with Python hmac/hashlib, key "test-secret".
        const val HMAC_PRESET_GET = "ae16ea5f00c12b4ad5d1664b8bb1670ad4481d81fa58ffcc1866e73f47753fa6"
        const val HMAC_PRESET = "28a836415752ac17a86f87982d89bcc4234cbfaa20d34a5b4e29512973725dfc"
        const val HMAC_PRESET_SET = "379d09196068c8562319ce7fae7506c7a275efa325c1aa5ffe32056b22b0ff34"
    }
}
