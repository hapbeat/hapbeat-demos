package com.hapbeat.demoremote.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class DemoSwitchProtocolTest {
    private val unsigned = AuthConfig(secret = null, allowUnsigned = true)
    private val signed = AuthConfig(secret = SECRET, allowUnsigned = false)

    // ---- canonical bytes ----

    @Test
    fun canonicalCommandMatchesSpecExample() {
        val expected = "HAPBEAT-DEMO-SWITCH/1\nCOMMAND\nversion=1:1\ntype=6:SWITCH\ncontroller_id=7:m5-main\nseq=2:42\ndemo_id=9:gloveball\n"
        assertEquals(expected, DemoSwitchProtocol.canonicalCommand("m5-main", 42, "gloveball"))
    }

    @Test
    fun canonicalControlKeepsEmptySceneId() {
        val c = DemoSwitchProtocol.canonicalControl("m5-main", 43, "volley", "menu_open", "")
        assertTrue(c.endsWith("action=9:menu_open\nscene_id=0:\n"))
    }

    @Test
    fun canonicalUsesUtf8ByteLength() {
        val c = DemoSwitchProtocol.canonicalStatus("FAILED", "m5-main", 1, "a", "a", "launch_failed", "é🙂")
        assertTrue(c.endsWith("message=6:é🙂\n"))
    }

    // ---- HMAC (expected values computed independently with Python hmac/hashlib) ----

    @Test
    fun hmacCommand() = assertEquals(
        "6d2ad5f032833d228f090ed7e8242fbfcd50c121f2a2905a5720cd8c15fcd412",
        DemoSwitchProtocol.hmacHex(SECRET, DemoSwitchProtocol.canonicalCommand("m5-main", 42, "gloveball")),
    )

    @Test
    fun hmacControl() {
        assertEquals(
            "246022bf2287eecc4d049f470a44fe34b0556949ca26ecc781039052f27f8152",
            DemoSwitchProtocol.hmacHex(SECRET, DemoSwitchProtocol.canonicalControl("m5-main", 43, "volley", "scene", "block")),
        )
        assertEquals(
            "2ba03e7ac8948d1aed9a8995bbe552b47f2aa4a009c5efd43540fc0362b3372f",
            DemoSwitchProtocol.hmacHex(SECRET, DemoSwitchProtocol.canonicalControl("android-0a1b2c3d", 7, "boxing", "menu_open", "")),
        )
    }

    @Test
    fun hmacStatus() = assertEquals(
        "aa84044bc4ea8a4403f5f4b7a62330ec482cf7ba4c41a5453715ee9fad8adc00",
        DemoSwitchProtocol.hmacHex(
            SECRET,
            DemoSwitchProtocol.canonicalStatus("FAILED", "m5-main", 43, "gloveball", "gloveball", "launch_failed", "起動失敗 🙂"),
        ),
    )

    @Test
    fun hmacDiscover() = assertEquals(
        "3a0df3b57bb91b2c880e39f8e7f508accdf0c660fc25bb2403f175a5a049eb08",
        DemoSwitchProtocol.hmacHex(SECRET, DemoSwitchProtocol.canonicalDiscover("m5-main", "0123456789abcdef")),
    )

    @Test
    fun hmacHere() = assertEquals(
        "2d2f33aa0fa16470caf1315afe4312824a4901928bec7443af214885555254c3",
        DemoSwitchProtocol.hmacHex(SECRET, DemoSwitchProtocol.canonicalHere("m5-main", "0123456789abcdef", "gloveball")),
    )

    // ---- outgoing JSON ----

    @Test
    fun buildSwitchFollowsSpecFieldOrder() {
        assertEquals(
            """{"version":1,"type":"SWITCH","controller_id":"m5-main","seq":42,"demo_id":"gloveball"}""",
            DemoSwitchProtocol.buildSwitch("m5-main", 42, "gloveball", unsigned),
        )
        assertEquals(
            """{"version":1,"type":"SWITCH","controller_id":"m5-main","seq":42,"demo_id":"gloveball",""" +
                """"auth":"6d2ad5f032833d228f090ed7e8242fbfcd50c121f2a2905a5720cd8c15fcd412"}""",
            DemoSwitchProtocol.buildSwitch("m5-main", 42, "gloveball", signed),
        )
    }

    @Test
    fun buildControlAndDiscover() {
        assertEquals(
            """{"version":1,"type":"CONTROL","controller_id":"m5-main","seq":43,"demo_id":"volley","action":"scene","scene_id":"block"}""",
            DemoSwitchProtocol.buildControl("m5-main", 43, "volley", "scene", "block", unsigned),
        )
        assertEquals(
            """{"version":1,"type":"DISCOVER","controller_id":"m5-main","nonce":"0123456789abcdef"}""",
            DemoSwitchProtocol.buildDiscover("m5-main", "0123456789abcdef", unsigned),
        )
        // Built messages parse back to the same values.
        val parsed = DemoSwitchProtocol.parse(DemoSwitchProtocol.buildControl("m5-main", 43, "volley", "restart", "", signed).toByteArray())
        assertTrue(parsed is DemoSwitchMessage.Control)
        assertTrue(DemoSwitchProtocol.authAccepted(parsed!!, signed))
    }

    @Test
    fun identifiersAndNonce() {
        assertTrue(DemoSwitchProtocol.isIdentifier("trex-encounter"))
        assertTrue(DemoSwitchProtocol.isIdentifier("gloveball_v2"))
        assertFalse(DemoSwitchProtocol.isIdentifier("-bad"))
        assertFalse(DemoSwitchProtocol.isIdentifier("Upper"))
        assertFalse(DemoSwitchProtocol.isIdentifier("a".repeat(65)))
        assertTrue(DemoSwitchProtocol.isIdentifier("a".repeat(64)))
        val nonce = DemoSwitchProtocol.newNonce()
        assertTrue(Regex("^[0-9a-f]{16}$").matches(nonce))
    }

    // ---- fixtures (hapbeat-contracts fixtures/sample-demo-switch-messages.json) ----

    @Test
    fun fixturesAreAcceptedInUnsignedMode() {
        for ((name, json) in FIXTURES) {
            val message = DemoSwitchProtocol.parse(json.toByteArray())
            assertNotNull(name, message)
            assertTrue(name, DemoSwitchProtocol.authAccepted(message!!, unsigned))
            assertFalse(name, DemoSwitchProtocol.authAccepted(message, AuthConfig(null, allowUnsigned = false)))
            assertFalse(name, DemoSwitchProtocol.authAccepted(message, signed))
        }
    }

    @Test
    fun utf8BoundaryMessage() {
        val ok = DemoSwitchProtocol.parse(statusWithMessage("🙂".repeat(64)).toByteArray())
        assertTrue(ok is DemoSwitchMessage.Status)
        assertEquals(256, (ok as DemoSwitchMessage.Status).message.toByteArray().size)
        assertNull(DemoSwitchProtocol.parse(statusWithMessage("🙂".repeat(64) + "a").toByteArray()))
    }

    @Test
    fun rejectsUnknownFieldsAndBadTypes() {
        assertNull(parse("""{"version":1,"type":"SWITCH","controller_id":"m5-main","seq":42,"demo_id":"gloveball","extra":1}"""))
        assertNull(parse("""{"version":1,"type":"HERE","controller_id":"m5-main","nonce":"0123456789abcdef","current_demo_id":"a","x":""}"""))
        assertNull(parse("""{"version":1,"type":"SWITCH","controller_id":"m5-main","seq":"42","demo_id":"gloveball"}"""))
        assertNull(parse("""{"version":1.0,"type":"SWITCH","controller_id":"m5-main","seq":42,"demo_id":"gloveball"}"""))
        assertNull(parse("""{"version":2,"type":"SWITCH","controller_id":"m5-main","seq":42,"demo_id":"gloveball"}"""))
        assertNull(parse("""{"version":1,"type":"SWITCH","controller_id":"m5-main","seq":0,"demo_id":"gloveball"}"""))
        assertNull(parse("""{"version":1,"type":"SWITCH","controller_id":"m5-main","seq":9007199254740992,"demo_id":"gloveball"}"""))
        assertNull(parse("""{"version":1,"type":"SWITCH","controller_id":"m5-main","seq":42}"""))
        assertNull(parse("""{"version":1,"type":"READY","controller_id":"m5-main","seq":42,"demo_id":"a","current_demo_id":"a","code":"weird","message":""}"""))
        assertNull(parse("""{"version":1,"type":"CONTROL","controller_id":"m5-main","seq":4,"demo_id":"a","action":"menu_open","scene_id":"x"}"""))
        assertNull(parse("""{"version":1,"type":"HERE","controller_id":"m5-main","nonce":"0123456789ABCDEF","current_demo_id":"a"}"""))
        assertNull(parse("""{"version":1,"type":"HERE","controller_id":"m5-main","nonce":"0123456789abcdef","current_demo_id":"a","auth":"XYZ"}"""))
        // duplicate key, two objects, trailing bytes, array root
        assertNull(parse("""{"version":1,"version":1,"type":"SWITCH","controller_id":"m5-main","seq":42,"demo_id":"g"}"""))
        assertNull(parse("""{"version":1,"type":"SWITCH","controller_id":"m5-main","seq":42,"demo_id":"g"}{}"""))
        assertNull(parse("""[1]"""))
        assertNull(DemoSwitchProtocol.parse(ByteArray(1025) { ' '.code.toByte() }))
    }

    // ---- receive-side validation ----

    @Test
    fun hereAcceptance() {
        val here = DemoSwitchMessage.Here("android-0a1b2c3d", "0123456789abcdef", "volley", null)
        val nonces = setOf("0123456789abcdef")
        assertTrue(DemoSwitchProtocol.acceptHere(here, "192.168.1.20", "android-0a1b2c3d", nonces, unsigned))
        assertFalse(DemoSwitchProtocol.acceptHere(here, "255.255.255.255", "android-0a1b2c3d", nonces, unsigned))
        assertFalse(DemoSwitchProtocol.acceptHere(here, "224.0.0.1", "android-0a1b2c3d", nonces, unsigned))
        assertFalse(DemoSwitchProtocol.acceptHere(here, "192.168.1.20", "android-ffffffff", nonces, unsigned))
        assertFalse(DemoSwitchProtocol.acceptHere(here, "192.168.1.20", "android-0a1b2c3d", setOf("ffffffffffffffff"), unsigned))
        assertFalse(DemoSwitchProtocol.acceptHere(here, "192.168.1.20", "android-0a1b2c3d", nonces, AuthConfig(null, false)))
        assertFalse(DemoSwitchProtocol.acceptHere(here, "192.168.1.20", "android-0a1b2c3d", nonces, signed))
        assertFalse(DemoSwitchProtocol.acceptHere(here, "192.168.1.20", "android-0a1b2c3d", nonces, unsigned, expectedSource = "192.168.1.21"))

        val auth = DemoSwitchProtocol.hmacHex(SECRET, DemoSwitchProtocol.canonicalHere("android-0a1b2c3d", "0123456789abcdef", "volley"))
        val signedHere = here.copy(auth = auth)
        assertTrue(DemoSwitchProtocol.acceptHere(signedHere, "192.168.1.20", "android-0a1b2c3d", nonces, signed))
        assertFalse(DemoSwitchProtocol.acceptHere(signedHere.copy(currentDemoId = "boxing"), "192.168.1.20", "android-0a1b2c3d", nonces, signed))
        // A signed reply is not accepted in unsigned mode (auth must be absent there).
        assertFalse(DemoSwitchProtocol.acceptHere(signedHere, "192.168.1.20", "android-0a1b2c3d", nonces, unsigned))
    }

    @Test
    fun statusAcceptance() {
        val status = DemoSwitchMessage.Status("READY", "m5-main", 42, "gloveball", "gloveball", "ok", "", null)
        val pending = { seq: Long, demo: String -> if (seq == 42L && demo == "gloveball") "10.0.0.5" else null }
        assertTrue(DemoSwitchProtocol.acceptStatus(status, "10.0.0.5", "m5-main", pending, unsigned))
        assertFalse(DemoSwitchProtocol.acceptStatus(status, "10.0.0.6", "m5-main", pending, unsigned))
        assertFalse(DemoSwitchProtocol.acceptStatus(status, "10.0.0.5", "other", pending, unsigned))
        assertFalse(DemoSwitchProtocol.acceptStatus(status.copy(seq = 41), "10.0.0.5", "m5-main", pending, unsigned))
        assertFalse(DemoSwitchProtocol.acceptStatus(status.copy(demoId = "boxing"), "10.0.0.5", "m5-main", pending, unsigned))
        assertFalse(DemoSwitchProtocol.acceptStatus(status, "10.0.0.5", "m5-main", pending, signed))

        val auth = DemoSwitchProtocol.hmacHex(SECRET, DemoSwitchProtocol.canonicalStatus("READY", "m5-main", 42, "gloveball", "gloveball", "ok", ""))
        assertTrue(DemoSwitchProtocol.acceptStatus(status.copy(auth = auth), "10.0.0.5", "m5-main", pending, signed))
        assertFalse(DemoSwitchProtocol.acceptStatus(status.copy(type = "FAILED", auth = auth), "10.0.0.5", "m5-main", pending, signed))
    }

    @Test
    fun unicastIpv4() {
        assertTrue(DemoSwitchProtocol.isUnicastIpv4("192.168.0.10"))
        assertFalse(DemoSwitchProtocol.isUnicastIpv4("0.0.0.0"))
        assertFalse(DemoSwitchProtocol.isUnicastIpv4("127.0.0.1"))
        assertFalse(DemoSwitchProtocol.isUnicastIpv4("239.1.1.1"))
        assertFalse(DemoSwitchProtocol.isUnicastIpv4("192.168.0.010"))
        assertFalse(DemoSwitchProtocol.isUnicastIpv4("192.168.0"))
    }

    private fun parse(json: String) = DemoSwitchProtocol.parse(json.toByteArray())

    private fun statusWithMessage(message: String) =
        """{"version":1,"type":"FAILED","controller_id":"m5-main","seq":43,"demo_id":"gloveball","current_demo_id":"gloveball",""" +
            """"code":"launch_failed","message":"$message"}"""

    private companion object {
        const val SECRET = "test-secret"

        val FIXTURES = mapOf(
            "unsigned_control" to """{"version":1,"type":"CONTROL","controller_id":"m5-main","seq":43,"demo_id":"volley","action":"scene","scene_id":"block"}""",
            "unsigned_command" to """{"version":1,"type":"SWITCH","controller_id":"m5-main","seq":42,"demo_id":"gloveball"}""",
            "unsigned_discover" to """{"version":1,"type":"DISCOVER","controller_id":"m5-main","nonce":"0123456789abcdef"}""",
            "unsigned_here" to """{"version":1,"type":"HERE","controller_id":"m5-main","nonce":"0123456789abcdef","current_demo_id":"gloveball"}""",
            "ready" to """{"version":1,"type":"READY","controller_id":"m5-main","seq":42,"demo_id":"gloveball","current_demo_id":"gloveball","code":"ok","message":""}""",
            "utf8_boundary_status" to """{"version":1,"type":"FAILED","controller_id":"m5-main","seq":43,"demo_id":"gloveball",""" +
                """"current_demo_id":"gloveball","code":"launch_failed","message":"${"🙂".repeat(64)}"}""",
        )
    }
}
