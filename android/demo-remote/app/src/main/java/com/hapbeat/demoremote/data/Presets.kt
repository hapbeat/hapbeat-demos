package com.hapbeat.demoremote.data

/**
 * One demo in a preset, same shape as a step of the Hub start extra (demo-session.md):
 * [options] descriptor option id → value (missing = the demo's default), [retry] false = no retry on failure.
 */
data class PresetStep(val demoId: String, val options: Map<String, String> = emptyMap(), val retry: Boolean = true)

/** A preset handed over from the web showcase (QR / link); written into one of the Hub's slots with PRESET_SET. */
data class RemotePreset(val name: String, val steps: List<PresetStep>)

/**
 * One of the Hub's presets 1..3 (demo-switch-control.md「Hub presets」): [name] "" = no name, [visible] whether the
 * Hub's top screen offers it, [steps] empty = the slot is empty. Stored on the Hub, so it differs per headset.
 */
data class HubPreset(val name: String, val visible: Boolean, val steps: List<PresetStep>)
