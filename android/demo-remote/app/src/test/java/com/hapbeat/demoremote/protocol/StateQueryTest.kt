package com.hapbeat.demoremote.protocol

import com.hapbeat.demoremote.DemoCatalog
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** QUERY / STATE (demo-switch-control.md「State query」), tutorial_start and the Hub session start command. */
class StateQueryTest {
    private val unsigned = AuthConfig(secret = null, allowUnsigned = true)
    private val signed = AuthConfig(secret = "test-secret", allowUnsigned = false)

    // fixtures/sample-demo-switch-messages.json: unsigned_query / unsigned_state
    private val fixtureState = """{"version":1,"type":"STATE","controller_id":"remote-pixel","nonce":"0123456789abcdef",""" +
        """"current_demo_id":"handdemo","foreground":true,"haptics_on":true,"haptics_ui":false,"recenter_ui":false,"paused":false,""" +
        """"step_index":1,"step_count":3}"""

    private fun parse(json: String) = DemoSwitchProtocol.parse(json.toByteArray())

    @Test
    fun fixtureQueryAndState() {
        val query = parse("""{"version":1,"type":"QUERY","controller_id":"remote-pixel","nonce":"0123456789abcdef"}""")
        assertTrue(query is DemoSwitchMessage.Query)
        val state = parse(fixtureState) as DemoSwitchMessage.State
        assertEquals("handdemo", state.currentDemoId)
        assertTrue(state.foreground)
        assertTrue(state.hapticsOn)
        assertFalse(state.paused)
        assertEquals(1L, state.stepIndex)
        assertEquals(3L, state.stepCount)
    }

    @Test
    fun buildQueryMatchesFixture() = assertEquals(
        """{"version":1,"type":"QUERY","controller_id":"remote-pixel","nonce":"0123456789abcdef"}""",
        DemoSwitchProtocol.buildQuery("remote-pixel", "0123456789abcdef", unsigned),
    )

    @Test
    fun stateRejectsBadValues() {
        assertNull(parse(fixtureState.replace("\"haptics_on\":true", "\"haptics_on\":1")))
        assertNull(parse(fixtureState.replace("\"step_index\":1", "\"step_index\":-2")))
        assertNull(parse(fixtureState.replace("\"step_count\":3", "\"step_count\":33")))
        assertNull(parse(fixtureState.replace("}", ",\"extra\":0}")))
        assertNull(parse(fixtureState.replace(",\"paused\":false", "")))
        assertNull(parse(fixtureState.replace("\"foreground\":true", "\"foreground\":\"true\"")))
    }

    @Test
    fun stateWithoutForegroundIsRejected() {
        // foreground is required since the contract change; the older STATE shape is invalid.
        assertNull(parse(fixtureState.replace("\"foreground\":true,", "")))
        val background = parse(fixtureState.replace("\"foreground\":true", "\"foreground\":false")) as DemoSwitchMessage.State
        assertFalse(background.foreground)
    }

    @Test
    fun hmacQueryAndState() {
        assertEquals(
            "db4e69ecdb83d50df13f1fef44e80b106dca70e7787a25d832c7b7d28f1b3abf",
            DemoSwitchProtocol.hmacHex("test-secret", DemoSwitchProtocol.canonicalQuery("remote-pixel", "0123456789abcdef")),
        )
        val state = DemoSwitchMessage.State(
            "remote-pixel", "0123456789abcdef", "handdemo", foreground = true, hapticsOn = true, hapticsUi = false, recenterUi = false,
            paused = false, stepIndex = -1, stepCount = 0, auth = null,
        )
        assertEquals(
            "37f5a4884c014734dd4c77b93b87418c3c3cd864e7bb0cd066d8e7016bb8b0b0",
            DemoSwitchProtocol.hmacHex("test-secret", DemoSwitchProtocol.canonicalState(state)),
        )
        // foreground sits between current_demo_id and haptics_on in the canonical bytes.
        assertTrue(DemoSwitchProtocol.canonicalState(state).contains("current_demo_id=8:handdemo\nforeground=4:true\nhaptics_on=4:true\n"))
        assertEquals(
            "6864d05e315970013450041be0a46d3e36c0eef5a71431bda2c194539473b0d1",
            DemoSwitchProtocol.hmacHex("test-secret", DemoSwitchProtocol.canonicalState(state.copy(foreground = false))),
        )
    }

    @Test
    fun stateAcceptance() {
        val state = parse(fixtureState) as DemoSwitchMessage.State
        val target = { nonce: String -> if (nonce == "0123456789abcdef") "192.168.0.37" else null }
        assertTrue(DemoSwitchProtocol.acceptState(state, "192.168.0.37", "remote-pixel", target, unsigned))
        assertFalse(DemoSwitchProtocol.acceptState(state, "192.168.0.38", "remote-pixel", target, unsigned))
        assertFalse(DemoSwitchProtocol.acceptState(state, "192.168.0.37", "other", target, unsigned))
        assertFalse(DemoSwitchProtocol.acceptState(state, "192.168.0.37", "remote-pixel", target, signed))
    }

    @Test
    fun tutorialStartIsAControlAction() {
        assertTrue(parse("""{"version":1,"type":"CONTROL","controller_id":"remote-pixel","seq":5,"demo_id":"handdemo","action":"tutorial_start","scene_id":""}""") is DemoSwitchMessage.Control)
    }

    @Test
    fun hubSessionCommands() {
        val prefix = "am start -n jp.hapbeat.demohub/com.unity3d.player.UnityPlayerGameActivity --es com.hapbeat.demo_hub.start "
        assertEquals(prefix + """'{"version":1,"preset":2}'""", DemoCatalog.hubSessionCommand(preset = 2))
        assertEquals(prefix + """'{"version":1,"demo_id":"handdemo"}'""", DemoCatalog.hubSessionCommand(demoId = "handdemo"))
        assertEquals(
            prefix + """'{"version":1,"demo_id":"handdemo","options":{"tutorial":"on"}}'""",
            DemoCatalog.hubSessionCommand(demoId = "handdemo", tutorial = "on"),
        )
    }

    @Test(expected = IllegalArgumentException::class)
    fun hubSessionRejectsUnknownDemo() {
        DemoCatalog.hubSessionCommand(demoId = "x'; reboot")
    }
}
