package com.hapbeat.demoremote.protocol

import com.hapbeat.demoremote.data.HubDemo
import com.hapbeat.demoremote.data.PresetStep
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * STATE's optional fields, the new CONTROL actions, HUB_SETTINGS_GET / HUB_SETTINGS / HUB_SETTINGS_SET / HUB_START
 * (demo-switch-control.md「State query」「Hub settings」「Hub start」). Expected HMACs are the reference values of
 * instructions-remote-hub-parity (key "test-secret", shared by the three implementations).
 */
class HubParityMessagesTest {
    private val unsigned = AuthConfig(secret = null, allowUnsigned = true)
    private val signed = AuthConfig(secret = "test-secret", allowUnsigned = false)

    private val stateExtended = """{"version":1,"type":"STATE","controller_id":"remote-pixel","nonce":"0123456789abcdef",""" +
        """"current_demo_id":"handdemo","foreground":true,"haptics_on":true,"haptics_ui":false,"recenter_ui":false,"paused":false,""" +
        """"step_index":1,"step_count":3,"device_model":"Oculus Quest 3","editor":false,"screen":"main","hand_style":"ghost"}"""
    private val statePartial = """{"version":1,"type":"STATE","controller_id":"remote-pixel","nonce":"0123456789abcdef",""" +
        """"current_demo_id":"handdemo","foreground":true,"haptics_on":true,"haptics_ui":false,"recenter_ui":false,"paused":false,""" +
        """"step_index":1,"step_count":3,"screen":"completion"}"""
    private val controlSkin = """{"version":1,"type":"CONTROL","controller_id":"remote-pixel","seq":48,"demo_id":"handdemo",""" +
        """"action":"hand_style_skin","scene_id":""}"""
    private val settingsGet = """{"version":1,"type":"HUB_SETTINGS_GET","controller_id":"remote-pixel","nonce":"0123456789abcdef","from":0}"""
    private val settings = """{"version":1,"type":"HUB_SETTINGS","controller_id":"remote-pixel","nonce":"0123456789abcdef","revision":3,""" +
        """"haptics_ui":false,"recenter_ui":true,"hand_style":"ghost","staff_waiting":false,"player":1,"group":-1,"demo_count":2,"from":0,""" +
        """"demos":[{"demo_id":"volley","title":"Volley","visible":true},{"demo_id":"fps","title":"FPS","visible":false}]}"""
    private val settingsSet = """{"version":1,"type":"HUB_SETTINGS_SET","controller_id":"remote-pixel","seq":46,"demo_id":"demo_hub",""" +
        """"haptics_ui":false,"recenter_ui":true,"hand_style":"skin","staff_waiting":false,"visible_demos":["volley"]}"""
    private val hubStart = """{"version":1,"type":"HUB_START","controller_id":"remote-pixel","seq":47,"demo_id":"demo_hub",""" +
        """"steps":[{"demo_id":"volley","options":{"scene":"match"}}]}"""

    private val demos = listOf(HubDemo("volley", "Volley", true), HubDemo("fps", "FPS", false))
    private val startSteps = listOf(PresetStep("volley", mapOf("scene" to "match")))

    private fun parse(json: String) = DemoSwitchProtocol.parse(json.toByteArray(Charsets.UTF_8))
    private fun hmac(message: DemoSwitchMessage) = DemoSwitchProtocol.hmacHex("test-secret", DemoSwitchProtocol.canonical(message))

    /** Closing `auth` field of a signed message (test vectors for key "test-secret", not secrets). */
    private fun withAuth(hmac: String) = ",\"" + "auth" + "\":\"" + hmac + "\"}"

    // ---- STATE ----

    @Test
    fun stateOptionalFieldsParse() {
        val full = parse(stateExtended) as DemoSwitchMessage.State
        assertEquals("Oculus Quest 3", full.deviceModel)
        assertEquals(false, full.editor)
        assertEquals("main", full.screen)
        assertEquals("ghost", full.handStyle)
        val partial = parse(statePartial) as DemoSwitchMessage.State
        assertNull(partial.deviceModel)
        assertNull(partial.editor)
        assertEquals("completion", partial.screen)
        assertNull(partial.handStyle)
    }

    @Test
    fun hmacState() {
        assertEquals("4722f50769469f635991a711f24fe04dd847136b02d1e6a2e241840d48b85cf1", hmac(parse(stateExtended)!!))
        assertEquals("c1529021a52273598596f8cefde5c5da6d58ee912bee05219f29c4e1569b1a74", hmac(parse(statePartial)!!))
        // Only present fields are signed, after step_count.
        assertTrue(DemoSwitchProtocol.canonical(parse(statePartial)!!).endsWith("step_count=1:3\nscreen=10:completion\n"))
    }

    @Test
    fun stateRejectsBadOptionalValues() {
        listOf(
            stateExtended.replace("\"Oculus Quest 3\"", "\"\""),
            stateExtended.replace("\"Oculus Quest 3\"", "\"${"q".repeat(65)}\""),
            stateExtended.replace("\"Oculus Quest 3\"", "\"Quest\\u0085\""),
            stateExtended.replace("\"Oculus Quest 3\"", "\"Quest\\t3\""),
            stateExtended.replace("\"editor\":false", "\"editor\":\"false\""),
            stateExtended.replace("\"screen\":\"main\"", "\"screen\":\"menu\""),
            stateExtended.replace("\"hand_style\":\"ghost\"", "\"hand_style\":\"robot\""),
            stateExtended.replace("\"hand_style\":\"ghost\"", "\"hand_style\":null"),
        ).forEach { assertNull(it, parse(it)) }
        // 64 code points (non-BMP counted once) are allowed.
        assertTrue(parse(stateExtended.replace("\"Oculus Quest 3\"", "\"${"😀".repeat(64)}\"")) is DemoSwitchMessage.State)
    }

    @Test
    fun stateAcceptanceWithOptionalFields() {
        val target = { nonce: String -> if (nonce == "0123456789abcdef") "192.168.0.37" else null }
        val signedState = parse(stateExtended.dropLast(1) + withAuth("4722f50769469f635991a711f24fe04dd847136b02d1e6a2e241840d48b85cf1")) as DemoSwitchMessage.State
        assertTrue(DemoSwitchProtocol.acceptState(signedState, "192.168.0.37", "remote-pixel", target, signed))
        assertFalse(DemoSwitchProtocol.acceptState(signedState.copy(handStyle = "skin"), "192.168.0.37", "remote-pixel", target, signed))
        assertFalse(DemoSwitchProtocol.acceptState(signedState.copy(editor = null), "192.168.0.37", "remote-pixel", target, signed))
    }

    // ---- CONTROL ----

    @Test
    fun newControlActions() {
        listOf("hand_style_ghost", "hand_style_skin", "session_next", "session_retry", "hub_top", "hub_replay").forEach { action ->
            assertTrue(action, parse(controlSkin.replace("hand_style_skin", action)) is DemoSwitchMessage.Control)
        }
        assertEquals(controlSkin, DemoSwitchProtocol.buildControl("remote-pixel", 48, "handdemo", "hand_style_skin", "", unsigned))
        assertEquals("19428bbb3df765bcb2606f517c3a41328574b5acd23537c306768ea4478eee80", hmac(parse(controlSkin)!!))
        assertNull(parse(controlSkin.replace("\"scene_id\":\"\"", "\"scene_id\":\"x\"")))
    }

    // ---- HUB_SETTINGS_GET / HUB_SETTINGS ----

    @Test
    fun hubSettingsParseAndBuild() {
        assertEquals(DemoSwitchMessage.HubSettingsGet("remote-pixel", "0123456789abcdef", 0, null), parse(settingsGet))
        assertEquals(settingsGet, DemoSwitchProtocol.buildHubSettingsGet("remote-pixel", "0123456789abcdef", 0, unsigned))
        assertEquals(
            DemoSwitchMessage.HubSettings("remote-pixel", "0123456789abcdef", 3, false, true, "ghost", false, 1, -1, 2, 0, demos, null),
            parse(settings),
        )
    }

    @Test
    fun hmacHubSettings() {
        assertEquals("de07246f7208dac8a116de59f8b2bf5f5e82135e5e276084a4b99664dc3d197a", hmac(parse(settingsGet)!!))
        assertEquals("volley;1;6:Volley|fps;0;3:FPS", DemoSwitchProtocol.canonicalHubDemos(demos))
        assertTrue(DemoSwitchProtocol.canonical(parse(settings)!!).endsWith("demos=29:volley;1;6:Volley|fps;0;3:FPS\n"))
        assertEquals("33d8ca3fbb0e14efae9e8e505992d3f97446bcdcfed7774953b367485a4d1a49", hmac(parse(settings)!!))
        assertTrue(
            DemoSwitchProtocol.buildHubSettingsGet("remote-pixel", "0123456789abcdef", 0, signed)
                .endsWith(withAuth("de07246f7208dac8a116de59f8b2bf5f5e82135e5e276084a4b99664dc3d197a")),
        )
    }

    @Test
    fun hubSettingsAcceptance() {
        val target = { nonce: String -> if (nonce == "0123456789abcdef") "192.168.0.37" else null }
        val page = parse(settings) as DemoSwitchMessage.HubSettings
        assertTrue(DemoSwitchProtocol.acceptHubSettings(page, "192.168.0.37", "remote-pixel", target, unsigned))
        assertFalse(DemoSwitchProtocol.acceptHubSettings(page, "192.168.0.38", "remote-pixel", target, unsigned))
        assertFalse(DemoSwitchProtocol.acceptHubSettings(page, "192.168.0.37", "other", target, unsigned))
        assertFalse(DemoSwitchProtocol.acceptHubSettings(page, "192.168.0.37", "remote-pixel", target, signed))
        val signedPage = parse(settings.dropLast(1) + withAuth("33d8ca3fbb0e14efae9e8e505992d3f97446bcdcfed7774953b367485a4d1a49")) as DemoSwitchMessage.HubSettings
        assertTrue(DemoSwitchProtocol.acceptHubSettings(signedPage, "192.168.0.37", "remote-pixel", target, signed))
        // A title change breaks the HMAC.
        val tampered = signedPage.copy(demos = listOf(HubDemo("volley", "Volley!", true), demos[1]))
        assertFalse(DemoSwitchProtocol.acceptHubSettings(tampered, "192.168.0.37", "remote-pixel", target, signed))
    }

    @Test
    fun hubSettingsRejectsSchemaViolations() {
        listOf(
            settings.replace("}]}", "}],\"extra\":0}"),
            settings.replace(",\"staff_waiting\":false", ""),
            settings.replace("\"demo_count\":2", "\"demo_count\":65"),
            settings.replace("\"from\":0", "\"from\":64"),
            settings.replace("\"player\":1", "\"player\":0"),
            settings.replace("\"group\":-1", "\"group\":100"),
            settings.replace("\"group\":-1", "\"group\":-2"),
            settings.replace("\"hand_style\":\"ghost\"", "\"hand_style\":\"Ghost\""),
            settings.replace("\"revision\":3", "\"revision\":-1"),
            settings.replace("\"title\":\"FPS\"", "\"title\":\"\""),
            settings.replace("\"title\":\"FPS\"", "\"title\":\"   \""),                   // blank (preset name rules)
            settings.replace("\"title\":\"FPS\"", "\"title\":\"F\\nPS\""),
            settings.replace("\"title\":\"FPS\"", "\"title\":\"F\\u2028PS\""),
            settings.replace("\"title\":\"FPS\"", "\"title\":\"${"x".repeat(41)}\""),
            settings.replace("\"visible\":false}", "\"visible\":false,\"x\":1}"),
            settings.replace("\"demo_id\":\"fps\"", "\"demo_id\":\"FPS\""),
            settingsGet.replace("\"from\":0", "\"from\":-1"),
            settingsGet.replace("}", ",\"preset\":1}"),
        ).forEach { assertNull(it, parse(it)) }
        assertTrue(parse(settings.replace("\"title\":\"FPS\"", "\"title\":\"${"あ".repeat(40)}\"")) is DemoSwitchMessage.HubSettings)
        assertTrue(parse(settingsGet.replace("\"from\":0", "\"from\":63")) is DemoSwitchMessage.HubSettingsGet)
    }

    // ---- HUB_SETTINGS_SET ----

    @Test
    fun hubSettingsSet() {
        assertEquals(
            DemoSwitchMessage.HubSettingsSet("remote-pixel", 46, "demo_hub", false, true, "skin", false, listOf("volley"), null),
            parse(settingsSet),
        )
        assertEquals(settingsSet, DemoSwitchProtocol.buildHubSettingsSet("remote-pixel", 46, false, true, "skin", false, listOf("volley"), unsigned))
        assertTrue(
            DemoSwitchProtocol.canonicalHubSettingsSet("remote-pixel", 46, false, true, "skin", false, listOf("volley"))
                .endsWith("visible_demos=6:volley\n"),
        )
        assertEquals("dd5dc8984242f56413c51d6b17ddfee52dd40e3dc663bfbfbd13533f8155039b", hmac(parse(settingsSet)!!))
        // No tiles: an empty list, signed as an empty value.
        val none = DemoSwitchProtocol.buildHubSettingsSet("remote-pixel", 47, true, false, "ghost", true, emptyList(), unsigned)
        assertTrue(none.endsWith("\"visible_demos\":[]}"))
        assertTrue(DemoSwitchProtocol.canonical(parse(none)!!).endsWith("visible_demos=0:\n"))
    }

    @Test
    fun hubSettingsSetRejectsSchemaViolations() {
        val ids = { n: Int -> settingsSet.replace("[\"volley\"]", "[" + (1..n).joinToString(",") { "\"d$it\"" } + "]") }
        assertTrue(parse(ids(64)) is DemoSwitchMessage.HubSettingsSet)
        listOf(
            ids(65),
            settingsSet.replace("[\"volley\"]", "[\"volley\",\"volley\"]"),
            settingsSet.replace("[\"volley\"]", "[\"Volley\"]"),
            settingsSet.replace("[\"volley\"]", "[1]"),
            settingsSet.replace("\"demo_id\":\"demo_hub\"", "\"demo_id\":\"volley\""),
            settingsSet.replace("\"hand_style\":\"skin\"", "\"hand_style\":\"\""),
            settingsSet.replace(",\"staff_waiting\":false", ""),
            settingsSet.replace("}", ",\"player\":1}"),
        ).forEach { assertNull(it, parse(it)) }
    }

    // ---- HUB_START ----

    @Test
    fun hubStartMessage() {
        assertEquals(DemoSwitchMessage.HubStart("remote-pixel", 47, "demo_hub", startSteps, null), parse(hubStart))
        assertEquals(hubStart, DemoSwitchProtocol.buildHubStart("remote-pixel", 47, startSteps, unsigned))
        assertTrue(DemoSwitchProtocol.canonicalHubStart("remote-pixel", 47, startSteps).endsWith("steps=20:volley;scene=match;1\n"))
        assertEquals("8cc476ef9a64e5b376aad77225b57429736f116063bdf4149601e111bec1e7a2", hmac(parse(hubStart)!!))
        val step = """{"demo_id":"handdemo"}"""
        val many = { n: Int -> hubStart.replace(Regex("\"steps\":\\[.*]"), "\"steps\":[" + List(n) { step }.joinToString(",") + "]") }
        assertTrue(parse(many(32)) is DemoSwitchMessage.HubStart)
        assertNull(parse(many(0)))
        assertNull(parse(many(33)))
        assertNull(parse(hubStart.replace("\"demo_id\":\"demo_hub\"", "\"demo_id\":\"volley\",\"x\":0")))
        assertNull(parse(hubStart.replace("\"seq\":47", "\"seq\":0")))
    }
}
