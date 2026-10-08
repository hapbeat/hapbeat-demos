package com.hapbeat.demoremote.adb

/**
 * Timing of Wi-Fi adb connects. A Quest whose display is off (not worn) keeps its Wi-Fi in power save and may miss ARP /
 * TCP SYN for seconds (EHOSTUNREACH or a timeout, both [AdbConnectResult.HostUnreachable]), so "no answer" is retried with
 * growing gaps for about [TOTAL_BUDGET_MS]. A refused port (Wi-Fi adb off) is final, except right after `adb tcpip` over
 * USB, where adbd is still restarting.
 */
object AdbRetryPolicy {
    /** No try is started once this much time has passed since the first one. */
    const val TOTAL_BUDGET_MS = 30_000L
    /** Gaps after try 1, 2, 3, …; the last one repeats. */
    val RETRY_DELAYS_MS = listOf(1_000L, 1_500L, 2_000L, 3_000L, 4_000L)
    /** UDP packets sent to wake the Quest's Wi-Fi before every try, and the gap between them. */
    const val WAKE_PACKETS = 3
    const val WAKE_INTERVAL_MS = 100L
    /** Tries without an answer after which a user-started connect shows the "Quest may be asleep" dialog. */
    const val SLEEP_HINT_AFTER = 2
    /** Gaps after automatic reconnect rounds (a lost connection); the last one repeats. */
    val RECONNECT_DELAYS_MS = listOf(5_000L, 10_000L, 20_000L, 30_000L, 60_000L)

    /** [result] may change on another try: no answer, or a refused port while adbd restarts ([retryPortClosed]). */
    fun isRetryable(result: AdbConnectResult, retryPortClosed: Boolean): Boolean =
        result == AdbConnectResult.HostUnreachable || (retryPortClosed && result == AdbConnectResult.PortClosed)

    /** Gap after try [attempt] (1-based). */
    fun retryDelayMs(attempt: Int): Long = RETRY_DELAYS_MS[(attempt - 1).coerceIn(0, RETRY_DELAYS_MS.lastIndex)]

    /**
     * Wait before the next try after try [attempt] (1-based) ended in [result], [elapsedMs] after the first try started;
     * null to give up: not retryable, or the next try would start at or past [TOTAL_BUDGET_MS].
     */
    fun nextDelayMs(attempt: Int, elapsedMs: Long, result: AdbConnectResult, retryPortClosed: Boolean): Long? {
        if (!isRetryable(result, retryPortClosed)) return null
        val wait = retryDelayMs(attempt)
        return if (elapsedMs + wait < TOTAL_BUDGET_MS) wait else null
    }

    /** [unanswered] tries got no answer: time to suggest waking the headset. */
    fun showSleepHint(unanswered: Int): Boolean = unanswered >= SLEEP_HINT_AFTER

    /** Gap after automatic reconnect round [round] (1-based). */
    fun reconnectDelayMs(round: Int): Long = RECONNECT_DELAYS_MS[(round - 1).coerceIn(0, RECONNECT_DELAYS_MS.lastIndex)]

    /** Status line while connecting; once a try got no answer it names the likely cause. */
    fun connectingText(seconds: Long, sleepSuspect: Boolean): String =
        if (sleepSuspect) "接続中（Quest の Wi-Fi が省電力中の可能性。$seconds 秒）" else "adb 接続中…（$seconds 秒）"
}
