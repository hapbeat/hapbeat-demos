package com.hapbeat.demoremote.data

import com.hapbeat.demoremote.protocol.AuthConfig
import com.hapbeat.demoremote.protocol.DemoSwitchMessage
import com.hapbeat.demoremote.protocol.DemoSwitchProtocol

/** One demo installed on the Hub (a Hub catalog entry): [title] is the Hub's display name, [visible] shown as a top tile. */
data class HubDemo(val demoId: String, val title: String, val visible: Boolean)

/**
 * The Hub-wide settings of the manage screen as last read with HUB_SETTINGS_GET (demo-switch-control.md「Hub settings」).
 * [player] / [group]: this headset's device address (-1 = not specified), read only. [demos]: all installed demos.
 */
data class HubSettingsSnapshot(
    val revision: Long,
    val hapticsUi: Boolean,
    val recenterUi: Boolean,
    val handStyle: String,
    val staffWaiting: Boolean,
    val player: Int,
    val group: Int,
    val demos: List<HubDemo>,
)

/** Outcome of reading the Hub-wide settings. */
sealed interface HubSettingsRead {
    data class Read(val settings: HubSettingsSnapshot) : HubSettingsRead
    /** A page got no HUB_SETTINGS (the Hub is not running, too old, or the reply was lost). */
    data object NoResponse : HubSettingsRead
    /** The pages do not fit together (wrong from, more demos than demo_count, an empty page) or the revision kept changing. */
    data object Inconsistent : HubSettingsRead
}

/**
 * Reading and checking the Hub-wide settings over Demo Switch (HUB_SETTINGS_GET / HUB_SETTINGS / HUB_SETTINGS_SET).
 * Pure Kotlin so the paging and validation rules run in JVM unit tests.
 */
object HubSettingsAccess {
    /** Restarts from 0 after a revision change at most this often before giving up. */
    const val MAX_REVISION_RESTARTS = 3

    /**
     * Reads the settings page by page: asks [fetch] from `from` = demos read so far until `demo_count` demos are in.
     * A revision change between pages starts again from 0. An empty page before `demo_count` cannot advance, so it is
     * inconsistent (one demo entry always fits in a datagram).
     */
    suspend fun read(fetch: suspend (from: Int) -> DemoSwitchMessage.HubSettings?): HubSettingsRead {
        var restarts = 0
        var revision: Long? = null
        val demos = mutableListOf<HubDemo>()
        while (true) {
            val from = demos.size
            val page = fetch(from) ?: return HubSettingsRead.NoResponse
            if (page.from != from) return HubSettingsRead.Inconsistent
            if (revision != null && page.revision != revision) {
                if (++restarts > MAX_REVISION_RESTARTS) return HubSettingsRead.Inconsistent
                revision = null
                demos.clear()
                continue
            }
            revision = page.revision
            if (from < page.demoCount && page.demos.isEmpty()) return HubSettingsRead.Inconsistent
            demos += page.demos
            if (demos.size > page.demoCount) return HubSettingsRead.Inconsistent
            if (demos.size == page.demoCount) {
                return HubSettingsRead.Read(HubSettingsSnapshot(
                    page.revision, page.hapticsUi, page.recenterUi, page.handStyle, page.staffWaiting, page.player, page.group,
                    demos.toList(),
                ))
            }
        }
    }

    /** The demos shown as tiles, in the Hub's order: the `visible_demos` of HUB_SETTINGS_SET. */
    fun visibleDemos(demos: List<HubDemo>): List<String> = demos.filter { it.visible }.map { it.demoId }

    /** Why [settings] cannot be written with HUB_SETTINGS_SET, or null: a known hand style and the 1024-byte limit. */
    fun problem(controllerId: String, settings: HubSettingsSnapshot): String? {
        if (settings.handStyle !in DemoSwitchProtocol.HAND_STYLES) return "手の見た目が正しくありません"
        if (worstCaseBytes(controllerId, settings) > DemoSwitchProtocol.MAX_PAYLOAD_BYTES) return "表示するデモが多すぎて送れません（Hub で変更してください）"
        return null
    }

    /** Bytes of the HUB_SETTINGS_SET for [settings] with the largest sequence and an `auth` field. */
    fun worstCaseBytes(controllerId: String, settings: HubSettingsSnapshot): Int = DemoSwitchProtocol.buildHubSettingsSet(
        controllerId, DemoSwitchProtocol.MAX_SEQ, settings.hapticsUi, settings.recenterUi, settings.handStyle, settings.staffWaiting,
        visibleDemos(settings.demos), WORST_CASE_AUTH,
    ).toByteArray(Charsets.UTF_8).size

    /** "プレイヤー 1・グループ 指定なし" for the device address read by the Hub. */
    fun addressText(player: Int, group: Int): String {
        val axis = { v: Int -> if (v < 0) "指定なし" else v.toString() }
        return "プレイヤー ${axis(player)}・グループ ${axis(group)}"
    }

    /** Any secret: the auth field is always 64 hex characters. */
    private val WORST_CASE_AUTH = AuthConfig(secret = "worst-case", allowUnsigned = false)
}
