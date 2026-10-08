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

    /**
     * Shown in the HMD list: not a responder that runs inside a development editor (STATE `editor`), unless
     * [showEditors] or it is the [selectedIp].
     */
    fun isListed(quest: QuestState, showEditors: Boolean, selectedIp: String?): Boolean =
        showEditors || quest.editor != true || quest.ip == selectedIp

    /** Label of an entry not confirmed by adb: "未確認 .37". */
    fun unconfirmedLabel(ip: String): String = "未確認 ." + ip.substringAfterLast('.')

    /** Label from STATE `device_model`: "Quest 3 · .37" (the "Oculus " / "Meta " prefix left out). */
    fun modelLabel(deviceModel: String, ip: String): String =
        deviceModel.removePrefix("Oculus ").removePrefix("Meta ").trim().ifEmpty { deviceModel } + " · ." + ip.substringAfterLast('.')

    /** A label the app gave on its own before adb confirmed the headset ([unconfirmedLabel] or a [modelLabel]). */
    fun isAutoLabel(label: String, ip: String, deviceModel: String?): Boolean =
        label == unconfirmedLabel(ip) || (deviceModel != null && label == modelLabel(deviceModel, ip))
}
