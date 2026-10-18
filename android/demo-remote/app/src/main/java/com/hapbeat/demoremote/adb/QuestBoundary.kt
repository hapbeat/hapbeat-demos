package com.hapbeat.demoremote.adb

/** The Quest boundary (Guardian) as read from [QuestBoundary.PROPERTY]. */
enum class BoundaryState { PAUSED, ACTIVE, UNKNOWN }

/**
 * Pauses / restores the Quest boundary over adb with `debug.oculus.guardian_pause` (not documented by Meta; checked on a
 * Quest 3S on 2026-10-18: 1 = no boundary, 0 or unset = boundary). Whether it survives a reboot is unknown, so the app
 * sets it again on every adb connect. Nothing is shown inside the headset.
 */
object QuestBoundary {
    const val PROPERTY = "debug.oculus.guardian_pause"

    const val GET_COMMAND = "getprop $PROPERTY"

    /** `setprop` that pauses ([paused] = true) or restores the boundary. */
    fun setCommand(paused: Boolean): String = "setprop $PROPERTY ${if (paused) 1 else 0}"

    /** `getprop` output -> state; null (adb not connected / the shell failed) or any other value is [BoundaryState.UNKNOWN]. */
    fun parse(output: String?): BoundaryState = when (output?.trim()) {
        null -> BoundaryState.UNKNOWN
        "1" -> BoundaryState.PAUSED
        "0", "" -> BoundaryState.ACTIVE
        else -> BoundaryState.UNKNOWN
    }

    /** Word for the status line "境界: …". */
    fun label(state: BoundaryState): String = when (state) {
        BoundaryState.PAUSED -> "なし"
        BoundaryState.ACTIVE -> "あり"
        BoundaryState.UNKNOWN -> "不明"
    }
}
