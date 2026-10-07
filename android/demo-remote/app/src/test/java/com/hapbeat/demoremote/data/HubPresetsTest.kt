package com.hapbeat.demoremote.data

import com.hapbeat.demoremote.protocol.DemoSwitchMessage
import com.hapbeat.demoremote.protocol.DemoSwitchProtocol
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Paging / revision rules of PRESET_GET and the checks before PRESET_SET (demo-switch-control.md「Hub presets」). */
class HubPresetsTest {
    private val id = "android-0a1b2c3d"

    private fun page(
        from: Int, steps: List<PresetStep>, stepCount: Int, revision: Long = 7, preset: Int = 1, name: String = "A", visible: Boolean = true,
    ) = DemoSwitchMessage.Preset("remote-pixel", "0123456789abcdef", preset, revision, name, visible, stepCount, from, steps, null)

    private fun demos(vararg ids: String) = ids.map { PresetStep(it) }

    /** Runs [HubPresets.read] against [answer] (from -> page) and records the `from` values asked for. */
    private fun read(number: Int = 1, answer: (Int) -> DemoSwitchMessage.Preset?): Pair<PresetRead, List<Int>> {
        val asked = mutableListOf<Int>()
        val result = runBlocking { HubPresets.read(number) { from -> asked += from; answer(from) } }
        return result to asked
    }

    // ---- reading ----

    @Test
    fun onePage() {
        val (result, asked) = read { page(0, demos("handdemo", "fps"), 2) }
        assertEquals(PresetRead.Read(HubPresetSlot(1, 7, HubPreset("A", true, demos("handdemo", "fps")), 2, unreadable = false)), result)
        assertEquals(listOf(0), asked)
    }

    @Test
    fun emptySlot() {
        val (result, _) = read { page(0, emptyList(), 0, name = "") }
        assertEquals(PresetRead.Read(HubPresetSlot(1, 7, HubPreset("", true, emptyList()), 0, unreadable = false)), result)
    }

    @Test
    fun pagesContinueFromTheStepsRead() {
        val all = demos("handdemo", "fps", "boxing", "volley", "safety-mill")
        val (result, asked) = read { from -> page(from, all.subList(from, minOf(from + 2, all.size)), all.size) }
        assertEquals(all, (result as PresetRead.Read).slot.preset.steps)
        assertEquals(listOf(0, 2, 4), asked)
    }

    @Test
    fun revisionChangeStartsAgainFromZero() {
        var revision = 7L
        val (result, asked) = read { from ->
            val p = page(from, demos(if (revision == 7L) "handdemo" else "fps"), 2, revision = revision)
            if (from == 1 && revision == 7L) { revision = 8; page(from, demos("boxing"), 2, revision = 8) } else p
        }
        // Page 2 came from revision 8: everything is read again from 0 at revision 8.
        assertEquals(listOf(0, 1, 0, 1), asked)
        val slot = (result as PresetRead.Read).slot
        assertEquals(8L, slot.revision)
        assertEquals(demos("fps", "fps"), slot.preset.steps)
    }

    @Test
    fun revisionThatKeepsChangingGivesUp() {
        var revision = 0L
        val (result, _) = read { from -> page(from, demos("handdemo"), 2, revision = revision++) }
        assertEquals(PresetRead.Inconsistent, result)
    }

    @Test
    fun stepTooLargeIsUnreadable() {
        val (result, asked) = read { from -> if (from == 0) page(0, demos("handdemo"), 3) else page(from, emptyList(), 3) }
        val slot = (result as PresetRead.Read).slot
        assertTrue(slot.unreadable)
        assertEquals(demos("handdemo"), slot.preset.steps)
        assertEquals(3, slot.stepCount)
        assertEquals(listOf(0, 1), asked)
    }

    @Test
    fun noReplyAndMismatchedPages() {
        assertEquals(PresetRead.NoResponse, read { null }.first)
        assertEquals(PresetRead.NoResponse, read { from -> if (from == 0) page(0, demos("fps"), 2) else null }.first)
        assertEquals(PresetRead.Inconsistent, read(number = 2) { page(0, demos("fps"), 1, preset = 1) }.first)
        assertEquals(PresetRead.Inconsistent, read { page(1, demos("fps"), 1) }.first)
        assertEquals(PresetRead.Inconsistent, read { page(0, demos("fps", "fps"), 1) }.first)
    }

    // ---- checks before PRESET_SET ----

    @Test
    fun validPresetHasNoProblem() {
        assertNull(HubPresets.problem(id, 1, HubPreset("XR Kaigi A", true, listOf(PresetStep("energy-duel", mapOf("tutorial" to "on"))))))
        // Empty name and no steps (clears the slot).
        assertNull(HubPresets.problem(id, 3, HubPreset("", false, emptyList())))
    }

    @Test
    fun problemsAreFound() {
        val ok = listOf(PresetStep("handdemo"))
        assertEquals("プリセットの番号が正しくありません", HubPresets.problem(id, 4, HubPreset("", true, ok)))
        assertTrue(HubPresets.problem(id, 1, HubPreset("  ", true, ok))!!.startsWith("名前"))
        assertTrue(HubPresets.problem(id, 1, HubPreset("a\nb", true, ok))!!.startsWith("名前"))
        assertTrue(HubPresets.problem(id, 1, HubPreset("x".repeat(41), true, ok))!!.startsWith("名前"))
        assertTrue(HubPresets.problem(id, 1, HubPreset("", true, List(33) { PresetStep("fps") }))!!.contains("32"))
        // Not a session demo (the Hub itself, or unknown to this app).
        assertTrue(HubPresets.problem(id, 1, HubPreset("", true, listOf(PresetStep("demo_hub"))))!!.contains("demo_hub"))
        assertTrue(HubPresets.problem(id, 1, HubPreset("", true, listOf(PresetStep("gloveball"))))!!.contains("gloveball"))
        // Outside the option table.
        assertEquals("Boxing にない設定があります", HubPresets.problem(id, 1, HubPreset("", true, listOf(PresetStep("boxing", mapOf("round" to "120"))))))
        assertEquals("Hand Demo にない設定があります", HubPresets.problem(id, 1, HubPreset("", true, listOf(PresetStep("handdemo", mapOf("tutorial" to "on"))))))
    }

    @Test
    fun tooLargeForOneDatagram() {
        val all = mapOf("tutorial" to "off", "round_seconds" to "60", "difficulty" to "strong", "mode" to "match")
        val preset = { n: Int -> HubPreset("あ".repeat(40), true, List(n) { PresetStep("energy-duel", all, retry = false) }) }
        // Grows by one step at a time until the worst case passes 1024 bytes.
        val fits = (0..32).last { HubPresets.remainingBytes(id, 1, preset(it)) >= 0 }
        assertTrue(fits < 32)
        assertNull(HubPresets.problem(id, 1, preset(fits)))
        assertTrue(HubPresets.problem(id, 1, preset(fits + 1))!!.startsWith("大きすぎて"))
        assertTrue(HubPresets.remainingBytes(id, 1, preset(fits + 1)) < 0)
        // The remaining bytes are those of the worst-case datagram.
        assertEquals(
            DemoSwitchProtocol.MAX_PAYLOAD_BYTES - DemoSwitchProtocol.presetSetWorstCaseBytes(id, 1, "あ".repeat(40), true, preset(fits).steps),
            HubPresets.remainingBytes(id, 1, preset(fits)),
        )
    }

    @Test
    fun inactiveOptionsAreLeftOut() {
        // Volley: points applies to scene block / match only, balls to receive only (descriptor `when`).
        assertEquals(
            listOf(PresetStep("volley", mapOf("scene" to "receive", "balls" to "20"))),
            HubPresets.normalize(listOf(PresetStep("volley", mapOf("scene" to "receive", "points" to "3", "balls" to "20")))),
        )
        assertEquals(
            listOf(PresetStep("volley", mapOf("points" to "3"), retry = false)),
            HubPresets.normalize(listOf(PresetStep("volley", mapOf("points" to "3", "balls" to "20"), retry = false))),
        )
    }

    @Test
    fun failureTexts() {
        assertEquals("Volley が HMD にインストールされていません", HubPresets.failureText("not_allowed", "volley"))
        assertTrue(HubPresets.failureText("invalid_payload", "energy-duel").startsWith("Energy Duel にない設定です"))
        assertEquals(HubPresets.HUB_NOT_FOREGROUND, HubPresets.failureText("not_allowed", DemoSwitchProtocol.NOT_IN_FOREGROUND_MESSAGE))
        assertTrue(HubPresets.failureText("not_allowed", "").startsWith("Hub が受け付けませんでした"))
        assertEquals("Hub で失敗しました: storage", HubPresets.failureText("launch_failed", "storage"))
        assertEquals("replay", HubPresets.failureText("replay", ""))
    }
}
