package com.hapbeat.demoremote

/**
 * Which Quest entries stay in the list. The list holds only headsets seen now; nothing of it is saved.
 */
object QuestVisibility {
    /**
     * Seen: the demo answered in one of the last two discovery rounds, port 5555 was open in the last scan, or adb is
     * connected / connecting / waiting for the headset's permission. A round that has not run yet for the entry (null:
     * just added or back from the background) still counts, so a new entry is judged only after two rounds.
     */
    fun isVisible(quest: QuestState): Boolean =
        quest.respondedLastRound != false || quest.respondedPrevRound != false ||
            quest.adbPortOpen == true || quest.adb != AdbState.DISCONNECTED

    /** [quest] is the headset chosen last time: by [serial] when one was saved, else by [ip]. */
    fun matchesRemembered(quest: QuestState, serial: String, ip: String): Boolean =
        if (serial.isNotEmpty()) quest.serial == serial else ip.isNotEmpty() && quest.ip == ip
}
