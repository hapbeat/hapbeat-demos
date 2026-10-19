package com.hapbeat.demoremote.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * DEMO_SETTINGS_GET / DEMO_SETTINGS / DEMO_SETTINGS_SET (demo-switch-control.md「Demo settings」). The JSON is hapbeat-contracts
 * fixtures/sample-demo-switch-messages.json `unsigned_demo_settings_*`; the HMACs (key "test-secret") are the reference values
 * of contracts tests/test_demo_switch_control.py, shared with the Unity implementation.
 */
class DemoSettingsMessagesTest {
    private val unsigned = AuthConfig(secret = null, allowUnsigned = true)
    private val signed = AuthConfig(secret = "test-secret", allowUnsigned = false)

    private val get = """{"version":1,"type":"DEMO_SETTINGS_GET","controller_id":"remote-pixel","nonce":"0123456789abcdef","target":"boxing"}"""
    private val reply = """{"version":1,"type":"DEMO_SETTINGS","controller_id":"remote-pixel","nonce":"0123456789abcdef","hub_id":"5c1e0a9b7d3f2468","revision":4,""" +
        """"target":"boxing","installed":true,"settings":{"haptics_db":"2","round":"55"}}"""
    private val set = """{"version":1,"type":"DEMO_SETTINGS_SET","controller_id":"remote-pixel","seq":48,"demo_id":"demo_hub",""" +
        """"target":"boxing","settings":{"haptics_db":"4"}}"""

    private fun parse(json: String) = DemoSwitchProtocol.parse(json.toByteArray(Charsets.UTF_8))
    private fun hmac(message: DemoSwitchMessage) = DemoSwitchProtocol.hmacHex("test-secret", DemoSwitchProtocol.canonical(message))

    /** Closing `auth` field of a signed message (test vectors for key "test-secret", not secrets). */
    private fun withAuth(json: String, hmac: String) = json.dropLast(1) + ",\"" + "auth" + "\":\"" + hmac + "\"}"

    @Test
    fun builtMessagesMatchTheFixturesAndHmacVectors() {
        assertEquals(get, DemoSwitchProtocol.buildDemoSettingsGet("remote-pixel", "0123456789abcdef", "boxing", unsigned))
        assertEquals(withAuth(get, "bbd39a11d9b607e9ce63b1541d102768c294f93b8d64b6e192147f0810d00c40"), DemoSwitchProtocol.buildDemoSettingsGet("remote-pixel", "0123456789abcdef", "boxing", signed))
        assertEquals(set, DemoSwitchProtocol.buildDemoSettingsSet("remote-pixel", 48, "boxing", mapOf("haptics_db" to "4"), unsigned))
        assertEquals(withAuth(set, "0e18d85624c98c0f63b39291176219c48a1e560e452a5398ddc28b05c3f3571e"), DemoSwitchProtocol.buildDemoSettingsSet("remote-pixel", 48, "boxing", mapOf("haptics_db" to "4"), signed))
        assertTrue(DemoSwitchProtocol.canonicalDemoSettingsSet("remote-pixel", 48, "boxing", mapOf("haptics_db" to "4"))
            .endsWith("demo_id=8:demo_hub\ntarget=6:boxing\nsettings=12:haptics_db=4\n"))
    }

    @Test
    fun replyParsesAndIsSignedSortedByOptionId() {
        val parsed = parse(reply) as DemoSwitchMessage.DemoSettings
        assertEquals(4L, parsed.revision)
        assertEquals("5c1e0a9b7d3f2468", parsed.hubId)
        assertTrue(DemoSwitchProtocol.canonical(parsed).contains("nonce=16:0123456789abcdef\nhub_id=16:5c1e0a9b7d3f2468\nrevision=1:4\n"))
        assertEquals("boxing", parsed.target)
        assertTrue(parsed.installed)
        assertEquals(mapOf("haptics_db" to "2", "round" to "55"), parsed.settings)
        assertEquals("0f4a3edbb7b34747ce095a8a96e608b7ac2d2bd9eeaebb06276be4ceff3bf519", hmac(parsed))
        assertTrue(DemoSwitchProtocol.canonical(parsed).endsWith("installed=4:true\nsettings=21:haptics_db=2|round=55\n"))
        // The order sent does not change the signature.
        assertEquals("0f4a3edbb7b34747ce095a8a96e608b7ac2d2bd9eeaebb06276be4ceff3bf519", hmac(parse(reply.replace(""""haptics_db":"2","round":"55"""", """"round":"55","haptics_db":"2"""")) as DemoSwitchMessage))
        val signedReply = parse(withAuth(reply, "0f4a3edbb7b34747ce095a8a96e608b7ac2d2bd9eeaebb06276be4ceff3bf519"))!!
        assertTrue(DemoSwitchProtocol.authAccepted(signedReply, signed))
        assertFalse(DemoSwitchProtocol.authAccepted(parse(withAuth(reply.replace("\"55\"", "\"30\""), "0f4a3edbb7b34747ce095a8a96e608b7ac2d2bd9eeaebb06276be4ceff3bf519"))!!, signed))
        assertEquals("bbd39a11d9b607e9ce63b1541d102768c294f93b8d64b6e192147f0810d00c40", hmac(parse(get)!!))
        assertEquals("0e18d85624c98c0f63b39291176219c48a1e560e452a5398ddc28b05c3f3571e", hmac(parse(set)!!))
    }

    @Test
    fun aDemoThatIsNotInstalledHasNoSettings() {
        val none = parse(reply.replace("\"installed\":true", "\"installed\":false").replace("""{"haptics_db":"2","round":"55"}""", "{}"))
            as DemoSwitchMessage.DemoSettings
        assertFalse(none.installed)
        assertTrue(none.settings.isEmpty())
        assertTrue(DemoSwitchProtocol.canonical(none).endsWith("installed=5:false\nsettings=0:\n"))
        assertNull(parse(reply.replace("\"installed\":true", "\"installed\":false")))
    }

    @Test
    fun schemaViolationsAreDropped() {
        val nine = (0..8).joinToString(",", "{", "}") { "\"o$it\":\"v\"" }
        listOf(
            get.replace("\"boxing\"", "\"Boxing\""), get.replace("\"target\":\"boxing\"", "\"target\":\"boxing\",\"from\":0"),
            reply.replace("\"5c1e0a9b7d3f2468\"", "\"5C1E0A9B7D3F2468\""), reply.replace(",\"hub_id\":\"5c1e0a9b7d3f2468\"", ""),
            reply.replace("\"revision\":4", "\"revision\":-1"), reply.replace("\"installed\":true", "\"installed\":\"yes\""),
            reply.replace("\"2\"", "2"), reply.replace("\"2\"", "\"a|b\""), reply.replace("""{"haptics_db":"2","round":"55"}""", nine),
            set.replace("{\"haptics_db\":\"4\"}", "{}"), set.replace("{\"haptics_db\":\"4\"}", nine), set.replace("\"4\"", "\"4=5\""),
            set.replace("\"demo_hub\"", "\"boxing\""), set.replace("\"seq\":48", "\"seq\":0"), set.replace("\"seq\":48", "\"nonce\":\"0123456789abcdef\""),
        ).forEach { assertNull(it, parse(it)) }
        val eight = (0..7).joinToString(",", "{", "}") { "\"o$it\":\"v\"" }
        assertEquals(8, (parse(set.replace("{\"haptics_db\":\"4\"}", eight)) as DemoSwitchMessage.DemoSettingsSet).settings.size)
    }

    @Test
    fun replyIsAcceptedOnlyFromTheAskedQuestForThatNonce() {
        val parsed = parse(reply) as DemoSwitchMessage.DemoSettings
        val target = { nonce: String -> if (nonce == "0123456789abcdef") "192.168.1.20" else null }
        assertTrue(DemoSwitchProtocol.acceptDemoSettings(parsed, "192.168.1.20", "remote-pixel", target, unsigned))
        assertFalse(DemoSwitchProtocol.acceptDemoSettings(parsed, "192.168.1.21", "remote-pixel", target, unsigned))
        assertFalse(DemoSwitchProtocol.acceptDemoSettings(parsed, "192.168.1.20", "remote-other", target, unsigned))
        assertFalse(DemoSwitchProtocol.acceptDemoSettings(parsed, "192.168.1.20", "remote-pixel", { null }, unsigned))
        assertFalse("Unsigned while a secret is set.", DemoSwitchProtocol.acceptDemoSettings(parsed, "192.168.1.20", "remote-pixel", target, signed))
    }
}
