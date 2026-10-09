package com.hapbeat.demoremote

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class PortDiagnosisTest {
    private fun app(pkg: String) = DemoCatalog.apps.first { it.packageName == pkg }
    private val volley = app("jp.hapbeat.volley")
    private val duel = app("jp.hapbeat.energyduel")

    @Test
    fun frontDemoReadsTheTopResumedActivity() {
        val output = "  topResumedActivity=ActivityRecord{2b4c1a1 u0 jp.hapbeat.demohub/com.unity3d.player.UnityPlayerGameActivity t1234}\n"
        assertEquals(DemoCatalog.HUB_ID, PortDiagnosis.frontDemo(output)?.demoId)
        val volleyFront = "topResumedActivity=ActivityRecord{9f u0 jp.hapbeat.volley/com.unity3d.player.UnityPlayerGameActivity t7}"
        assertEquals("volley", PortDiagnosis.frontDemo(volleyFront)?.demoId)
    }

    @Test
    fun frontDemoIsNullForOtherApps() {
        assertNull(PortDiagnosis.frontDemo("topResumedActivity=ActivityRecord{1 u0 com.oculus.vrshell/.MainActivity t2}"))
        assertNull(PortDiagnosis.frontDemo(""))
    }

    @Test
    fun runningOthersLeavesTheFrontOutAndParsesKnownPackagesOnly() {
        val command = PortDiagnosis.runningOthersCommand(DemoCatalog.HUB_PACKAGE)
        assertFalse(command.contains(DemoCatalog.HUB_PACKAGE))
        assertTrue(command.contains("jp.hapbeat.volley") && command.contains("pidof"))
        val running = PortDiagnosis.parseRunning("jp.hapbeat.volley\r\ncom.example.other\n${DemoCatalog.HUB_PACKAGE}\n", DemoCatalog.HUB_PACKAGE)
        assertEquals(listOf(volley), running)
    }

    @Test
    fun logCommandFiltersTheFrontProcess() {
        val command = PortDiagnosis.logCommand(DemoCatalog.HUB_PACKAGE)
        assertTrue(command.startsWith("p=\$(pidof -s ${DemoCatalog.HUB_PACKAGE});"))
        assertTrue(command.contains("logcat -d"))
        assertTrue(command.contains("DEMO_SWITCH_BIND_WAITING"))
    }

    @Test
    fun relevantLinesKeepBindAndRejectLinesOnly() {
        val output = """
            10-10 01:00:00.000 I Unity: [Demo Switch] Unsigned mode is enabled. Use only on an isolated demo LAN.
            10-10 01:00:00.100 W Unity: [Demo Switch] Could not bind UDP 7710 (Address already in use); retrying every 1 s while in the foreground.
            10-10 01:00:05.100 W Unity: [Demo Switch] Waiting for UDP 7710: still not bound after 5 s (Address already in use).
        """.trimIndent()
        assertEquals(2, PortDiagnosis.relevantLines(output).size)
    }

    @Test
    fun bindFailureWithADemoRunningIsTheBackgroundDemo() {
        val lines = listOf("W Unity: [Demo Switch] Could not bind UDP 7710 (Address already in use)", "W Unity: [Demo Switch] Waiting for UDP 7710: ...")
        val cause = PortDiagnosis.classify(lines, listOf(volley))
        assertEquals(PortCause.BackgroundDemo(listOf(volley)), cause)
        assertEquals("裏で Volley が動いていて、受信ポートを使っています", PortDiagnosis.text(cause))
    }

    @Test
    fun unrealBindEventsCountAsBindFailures() {
        val lines = listOf("LogTemp: Warning: DEMO_SWITCH_BIND_RETRY port=7710", "LogTemp: Warning: DEMO_SWITCH_BIND_WAITING port=7710")
        assertEquals(PortCause.BackgroundDemo(listOf(duel)), PortDiagnosis.classify(lines, listOf(duel)))
    }

    @Test
    fun aDemoRunningWithoutLogsIsStillTheBackgroundDemo() {
        assertEquals(PortCause.BackgroundDemo(listOf(volley, duel)), PortDiagnosis.classify(emptyList(), listOf(volley, duel)))
        assertEquals("裏で Volley・Energy Duel が動いていて、受信ポートを使っています",
            PortDiagnosis.text(PortCause.BackgroundDemo(listOf(volley, duel))))
    }

    @Test
    fun rejectedCommandsAreAnAuthenticationMismatch() {
        listOf(
            "W Unity: [Demo Switch] Rejected DISCOVER payload: invalid_auth",
            "W Unity: [Demo Switch] Rejected QUERY payload: unsigned_disabled",
        ).forEach { line ->
            assertEquals(PortCause.AuthMismatch, PortDiagnosis.classify(listOf(line), emptyList()))
        }
        // A demo in the background but a bound receiver that rejects: authentication, not the port.
        val bound = listOf("I Unity: [Demo Switch] Bound UDP 7710 after 3 retries.", "W Unity: [Demo Switch] Rejected DISCOVER payload: invalid_auth")
        assertEquals(PortCause.AuthMismatch, PortDiagnosis.classify(bound, listOf(volley)))
        assertEquals("認証の設定が Quest 側と違います（共有の秘密鍵 / 鍵なしの許可を確認）", PortDiagnosis.text(PortCause.AuthMismatch))
    }

    @Test
    fun aLaterBindFailureWinsOverAnOlderBind() {
        val lines = listOf(
            "I Unity: [Demo Switch] Bound UDP 7710 after 1 retries.",
            "W Unity: [Demo Switch] Rejected DISCOVER payload: invalid_auth",
            "W Unity: [Demo Switch] Could not bind UDP 7710 (Address already in use)",
        )
        assertEquals(PortCause.BackgroundDemo(listOf(volley)), PortDiagnosis.classify(lines, listOf(volley)))
    }

    @Test
    fun nothingRunningAndNoRejectIsUnknownWithTheLastLines() {
        val lines = listOf("a [Demo Switch] Could not bind UDP 7710 (x)", "b [Demo Switch] Could not bind UDP 7710 (y)", "c [Demo Switch] Waiting for UDP 7710: z")
        val cause = PortDiagnosis.classify(lines, emptyList())
        assertEquals(PortCause.Unknown(lines.takeLast(2)), cause)
        assertTrue(PortDiagnosis.text(cause).startsWith("原因不明（ログ: b "))
        assertEquals("原因不明（ログ: なし）", PortDiagnosis.text(PortDiagnosis.classify(emptyList(), emptyList())))
    }

    @Test
    fun onlyTheLastLinesDecide() {
        val old = List(PortDiagnosis.TAIL_LINES) { "W Unity: [Demo Switch] Rejected DISCOVER payload: invalid_auth" }
        val lines = listOf("W Unity: [Demo Switch] Rejected QUERY payload: invalid_auth") + List(PortDiagnosis.TAIL_LINES) { "I Unity: [Demo Switch] Bound UDP 7710 after 1 retries." }
        assertEquals(PortCause.AuthMismatch, PortDiagnosis.classify(old, emptyList()))
        assertTrue(PortDiagnosis.classify(lines, emptyList()) is PortCause.Unknown)
    }

    @Test
    fun restartStopsAndStartsTheSameApp() {
        val command = DemoCatalog.restartCommand(DemoCatalog.hub)
        assertTrue(command.startsWith("am force-stop ${DemoCatalog.HUB_PACKAGE}; for p in "))
        assertTrue(command.endsWith(DemoCatalog.launchCommand(DemoCatalog.hub)))
    }
}
