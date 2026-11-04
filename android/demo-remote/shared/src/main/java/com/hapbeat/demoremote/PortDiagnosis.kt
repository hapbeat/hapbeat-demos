package com.hapbeat.demoremote

/**
 * Why a known demo in front of the headset (adb) does not answer Demo Switch. Found from the demos still running in
 * the background (`pidof`) and the front app's last Demo Switch log lines (demo-switch-control.md「Transport and
 * lifecycle」: a demo started by adb `am start` skips the hand-over and keeps UDP 7710 bound in the background).
 */
sealed class PortCause {
    /** Other known demos are running ([demos]); one of them holds UDP 7710. */
    data class BackgroundDemo(val demos: List<DemoApp>) : PortCause()

    /** The receiver rejects this remote's commands: shared secret / unsigned mode differ. */
    data object AuthMismatch : PortCause()

    /** Nothing explains it; [logLines]: the last relevant lines (may be empty). */
    data class Unknown(val logLines: List<String>) : PortCause()
}

object PortDiagnosis {
    /** The front activity of the headset (one `topResumedActivity=ActivityRecord{... u0 package/activity ...}` line). */
    const val TOP_ACTIVITY_COMMAND = "dumpsys activity activities | grep topResumedActivity"

    /** How many of the last relevant log lines decide the cause. */
    const val TAIL_LINES = 5

    private val TOP_PACKAGE = Regex("""\bu\d+\s+([A-Za-z0-9_.]+)/""")
    private val BIND_FAILURE = listOf(
        "Could not bind", "Waiting for UDP 7710", "DEMO_SWITCH_BIND_RETRY", "DEMO_SWITCH_LISTENER_FAILED", "DEMO_SWITCH_BIND_WAITING",
    )
    private const val BOUND = "Bound UDP"
    private val AUTH_REJECT = listOf("invalid_auth", "unsigned_disabled", "Rejected")

    /** The known demo (Hub included) in front according to [TOP_ACTIVITY_COMMAND]'s [output], or null. */
    fun frontDemo(output: String): DemoApp? {
        val pkg = output.lineSequence().firstNotNullOfOrNull { TOP_PACKAGE.find(it)?.groupValues?.get(1) } ?: return null
        return DemoCatalog.apps.firstOrNull { it.packageName == pkg }
    }

    /** Shell command that prints the package of every known demo running except [frontPackage], one per line. */
    fun runningOthersCommand(frontPackage: String): String {
        val others = DemoCatalog.apps.map { it.packageName }.filter { it.isNotEmpty() && it != frontPackage }.distinct()
        return "for p in ${others.joinToString(" ")}; do pidof \$p >/dev/null && echo \$p; done; true"
    }

    /** Known demos except [frontPackage] listed in [runningOthersCommand]'s [output]. */
    fun parseRunning(output: String, frontPackage: String): List<DemoApp> {
        val packages = output.lineSequence().map { it.trim() }.toSet()
        return DemoCatalog.apps.filter { it.packageName.isNotEmpty() && it.packageName != frontPackage && it.packageName in packages }
    }

    /**
     * Shell command that prints the front app's last Demo Switch log lines (Unity `[Demo Switch]`, Unreal
     * `DEMO_SWITCH_*` bind events) from `logcat -d`; the whole buffer when the app has no process.
     */
    fun logCommand(frontPackage: String): String =
        "p=\$(pidof -s $frontPackage); logcat -d \${p:+--pid=\$p} | " +
            "grep -E '\\[Demo Switch\\]|DEMO_SWITCH_BIND_RETRY|DEMO_SWITCH_LISTENER_FAILED|DEMO_SWITCH_BIND_WAITING' | tail -n 40"

    /** Lines of [output] that tell about the bind or a rejected command, in order. */
    fun relevantLines(output: String): List<String> = output.lineSequence().map { it.trim() }.filter { line ->
        line.isNotEmpty() && (BIND_FAILURE.any { line.contains(it) } || line.contains(BOUND) || AUTH_REJECT.any { line.contains(it) })
    }.toList()

    /**
     * The cause from the front app's [logLines] ([relevantLines]) and the other demos [running]. A bind failure newer
     * than the last successful bind, or no sign of an authentication problem, with demos running: [PortCause.BackgroundDemo].
     * A rejected command in the last [TAIL_LINES]: [PortCause.AuthMismatch]. Otherwise [PortCause.Unknown].
     */
    fun classify(logLines: List<String>, running: List<DemoApp>): PortCause {
        val tail = logLines.takeLast(TAIL_LINES)
        val lastFailure = tail.indexOfLast { line -> BIND_FAILURE.any { line.contains(it) } }
        val lastBound = tail.indexOfLast { it.contains(BOUND) }
        val bindFailing = lastFailure >= 0 && lastFailure > lastBound
        val rejected = tail.any { line -> AUTH_REJECT.any { line.contains(it) } }
        return when {
            running.isNotEmpty() && (bindFailing || !rejected) -> PortCause.BackgroundDemo(running)
            rejected -> PortCause.AuthMismatch
            else -> PortCause.Unknown(tail.takeLast(2))
        }
    }

    /** The reason shown to the user. */
    fun text(cause: PortCause): String = when (cause) {
        is PortCause.BackgroundDemo -> "裏で ${cause.demos.joinToString("・") { it.label }} が動いていて、受信ポートを使っています"
        PortCause.AuthMismatch -> "認証の設定が Quest 側と違います（共有の秘密鍵 / 鍵なしの許可を確認）"
        is PortCause.Unknown -> "原因不明（ログ: ${if (cause.logLines.isEmpty()) "なし" else cause.logLines.joinToString(" / ") { it.take(120) }}）"
    }
}
