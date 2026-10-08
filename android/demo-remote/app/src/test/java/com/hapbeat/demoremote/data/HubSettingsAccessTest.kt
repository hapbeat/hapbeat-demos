package com.hapbeat.demoremote.data

import com.hapbeat.demoremote.DemoCatalog
import com.hapbeat.demoremote.protocol.DemoSwitchMessage
import com.hapbeat.demoremote.protocol.DemoSwitchProtocol
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Paging of HUB_SETTINGS, the checks before HUB_SETTINGS_SET and the Hub-installed demos in presets / starts. */
class HubSettingsAccessTest {
    private val id = "android-0a1b2c3d"

    private fun demos(vararg ids: String) = ids.map { HubDemo(it, it.uppercase(), visible = true) }

    private fun page(from: Int, demos: List<HubDemo>, count: Int, revision: Long = 3) = DemoSwitchMessage.HubSettings(
        "remote-pixel", "0123456789abcdef", revision, false, true, "ghost", false, 1, -1, count, from, demos, null,
    )

    private fun read(answer: (Int) -> DemoSwitchMessage.HubSettings?): Pair<HubSettingsRead, List<Int>> {
        val asked = mutableListOf<Int>()
        val result = runBlocking { HubSettingsAccess.read { from -> asked += from; answer(from) } }
        return result to asked
    }

    // ---- reading ----

    @Test
    fun onePage() {
        val (result, asked) = read { page(0, demos("volley", "fps"), 2) }
        assertEquals(
            HubSettingsRead.Read(HubSettingsSnapshot(3, false, true, "ghost", false, 1, -1, demos("volley", "fps"))),
            result,
        )
        assertEquals(listOf(0), asked)
    }

    @Test
    fun noDemos() {
        val (result, _) = read { page(0, emptyList(), 0) }
        assertEquals(emptyList<HubDemo>(), (result as HubSettingsRead.Read).settings.demos)
    }

    @Test
    fun pagesContinueFromTheDemosRead() {
        val all = demos("a", "b", "c", "d", "e")
        val (result, asked) = read { from -> page(from, all.subList(from, minOf(from + 2, all.size)), all.size) }
        assertEquals(all, (result as HubSettingsRead.Read).settings.demos)
        assertEquals(listOf(0, 2, 4), asked)
    }

    @Test
    fun revisionChangeStartsAgainFromZero() {
        var revision = 3L
        val (result, asked) = read { from ->
            if (from == 1 && revision == 3L) { revision = 4; page(1, demos("b"), 2, revision = 4) }
            else page(from, demos(if (from == 0) "a" else "b"), 2, revision = revision)
        }
        assertEquals(listOf(0, 1, 0, 1), asked)
        assertEquals(4L, (result as HubSettingsRead.Read).settings.revision)
    }

    @Test
    fun badPages() {
        assertEquals(HubSettingsRead.NoResponse, read { null }.first)
        assertEquals(HubSettingsRead.NoResponse, read { from -> if (from == 0) page(0, demos("a"), 2) else null }.first)
        assertEquals(HubSettingsRead.Inconsistent, read { page(1, demos("a"), 1) }.first)
        assertEquals(HubSettingsRead.Inconsistent, read { page(0, demos("a", "b"), 1) }.first)
        // An empty page before demo_count cannot advance.
        assertEquals(HubSettingsRead.Inconsistent, read { page(it, emptyList(), 2) }.first)
        var revision = 0L
        assertEquals(HubSettingsRead.Inconsistent, read { from -> page(from, demos("a"), 2, revision = revision++) }.first)
    }

    // ---- HUB_SETTINGS_SET ----

    @Test
    fun visibleDemosAndProblems() {
        val settings = HubSettingsSnapshot(3, false, true, "skin", false, 1, -1,
            listOf(HubDemo("volley", "Volley", true), HubDemo("fps", "FPS", false), HubDemo("boxing", "Boxing", true)))
        assertEquals(listOf("volley", "boxing"), HubSettingsAccess.visibleDemos(settings.demos))
        assertNull(HubSettingsAccess.problem(id, settings))
        assertTrue(HubSettingsAccess.problem(id, settings.copy(handStyle = "robot"))!!.startsWith("手の見た目"))
        // 64 visible demos with long IDs do not fit in one datagram.
        val many = settings.copy(demos = (1..64).map { HubDemo("demo-${"x".repeat(50)}-$it", "D", true) })
        assertTrue(HubSettingsAccess.worstCaseBytes(id, many) > DemoSwitchProtocol.MAX_PAYLOAD_BYTES)
        assertTrue(HubSettingsAccess.problem(id, many)!!.contains("多すぎ"))
    }

    @Test
    fun addressText() {
        assertEquals("プレイヤー 1・グループ 指定なし", HubSettingsAccess.addressText(1, -1))
        assertEquals("プレイヤー 指定なし・グループ 12", HubSettingsAccess.addressText(-1, 12))
    }

    // ---- demos installed on the Hub ----

    @Test
    fun candidatesFollowTheHub() {
        assertEquals(DemoCatalog.sessionApps, DemoCatalog.sessionCandidates(null))
        val hub = listOf(HubDemo("fps", "FPS (Hub)", true), HubDemo("gloveball", "Glove Ball", false))
        val candidates = DemoCatalog.sessionCandidates(hub)
        assertEquals(listOf("fps", "gloveball"), candidates.map { it.demoId })
        // Known demos keep this app's entry (thumbnail, options); unknown ones use the Hub's name and no package.
        assertEquals(DemoCatalog.sessionApps.first { it.demoId == "fps" }, candidates[0])
        assertEquals("Glove Ball", candidates[1].label)
        assertEquals("", candidates[1].packageName)
        assertEquals("Glove Ball", DemoCatalog.labelFor("gloveball", hub))
        assertEquals("gloveball", DemoCatalog.labelFor("gloveball"))
    }

    @Test
    fun presetStepsCheckedAgainstTheHub() {
        val hub = listOf(HubDemo("gloveball", "Glove Ball", true), HubDemo("boxing", "Boxing", true))
        val preset = { steps: List<PresetStep> -> HubPreset("", true, steps) }
        // A demo unknown to this app but installed on the Hub, with its defaults.
        assertNull(HubPresets.problem(id, 1, preset(listOf(PresetStep("gloveball"))), hub))
        assertTrue(HubPresets.problem(id, 1, preset(listOf(PresetStep("gloveball", mapOf("level" to "2")))), hub)!!.contains("Glove Ball"))
        // A known demo the Hub does not have.
        assertTrue(HubPresets.problem(id, 1, preset(listOf(PresetStep("fps"))), hub)!!.contains("Hub に入っていません"))
        // Known demos still use their option table.
        assertNull(HubPresets.problem(id, 1, preset(listOf(PresetStep("boxing", mapOf("round" to "60")))), hub))
        assertEquals("Boxing にない設定があります", HubPresets.problem(id, 1, preset(listOf(PresetStep("boxing", mapOf("round" to "120")))), hub))
        // The Hub names an installed demo unknown to this app in its FAILED message.
        assertEquals("Glove Ball が HMD にインストールされていません", HubPresets.failureText("not_allowed", "gloveball", hub))
    }
}
