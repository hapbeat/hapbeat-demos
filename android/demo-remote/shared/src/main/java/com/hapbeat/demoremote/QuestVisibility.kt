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

    /** How long a new Demo Switch responder stays out of the list while its STATE (device model, editor) is awaited. */
    const val STATE_WAIT_MS = 1500L

    /**
     * Shown in the HMD list: not a responder that runs inside a development editor (STATE `editor`), unless
     * [showEditors] or it is the [selectedIp]. A responder that only answered DISCOVER is held back for [STATE_WAIT_MS]
     * until its STATE (or adb) tells what it is, so a PC editor never flashes up as a headset; after that it is listed
     * as "未確認" (a runtime without the STATE fields).
     */
    fun isListed(quest: QuestState, showEditors: Boolean, selectedIp: String?, nowMs: Long): Boolean {
        if (showEditors || quest.ip == selectedIp) return true
        if (quest.editor == true) return false
        val identified = quest.editor != null || quest.remoteState != null || quest.deviceModel != null ||
            quest.model.isNotEmpty() || quest.adb != AdbState.DISCONNECTED || quest.adbPortOpen == true
        return identified || quest.addedAtMs == 0L || nowMs - quest.addedAtMs >= STATE_WAIT_MS
    }

    /** Label of an entry not confirmed by adb: "未確認 .37". */
    fun unconfirmedLabel(ip: String): String = "未確認 ." + ip.substringAfterLast('.')

    /** Label from STATE `device_model`: "Quest 3 · .37" (the "Oculus " / "Meta " prefix left out). */
    fun modelLabel(deviceModel: String, ip: String): String =
        deviceModel.removePrefix("Oculus ").removePrefix("Meta ").trim().ifEmpty { deviceModel } + " · ." + ip.substringAfterLast('.')

    /** A label the app gave on its own before adb confirmed the headset ([unconfirmedLabel] or a [modelLabel]). */
    fun isAutoLabel(label: String, ip: String, deviceModel: String?): Boolean =
        label == unconfirmedLabel(ip) || (deviceModel != null && label == modelLabel(deviceModel, ip))
}
