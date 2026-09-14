# GloveBall: ball physics and audible impacts

Select `unity/gloveball/Assets/Resources/BallFeelSettings.asset` in Unity.

- `Balls / Kind`: explicit ball type (keep one entry per kind).
- `Mass`: Rigidbody mass. Gameplay values, not regulation ball weights.
- `Air Resistance`: Rigidbody linear damping. Foam and perforated balls slow down visibly.
- `Bounce Material`: edit its Bounciness/Friction to change rebounds; the contacted surface's material/combine setting also affects the result.
- `Impact Clip / Impact Volume`: audible player-impact sound per ball, independent of haptic clips and left/right targeting.
- `First Round Speed Multiplier / Final Round Speed Multiplier`: defaults .85/.95. Multiply Game's existing first/final-round speed bands; intermediate rounds interpolate. Endless speeds remain at Game's existing settings. An unreachable slow shot is clamped to the ballistic minimum.

Initial gameplay tuning:

| Kind | Mass | Damping | Bounciness |
|---|---:|---:|---:|
| Bowling | 1.2 | .02 | .25 |
| Volleyball | .27 | .08 | .65 |
| Foam | .06 | .65 | .22 |
| Basketball | .62 | .04 | .8 |
| Perforated | .04 | .4 | .48 |

Size and grab/throw ownership remain unchanged. Launcher shots compensate damping to retain the intended arrival point and flight time; light balls depart faster then slow down. Hand-thrown balls use the original release velocity and the same damping, so light balls have shorter range. Mass alone does not change gravity acceleration. This is simplified linear drag, not a full aerodynamic simulation.

Audible sounds are applied to the existing player arm/body impact pathway. Target/launcher sounds remain unchanged. Haptics are still configured separately in `Assets/GloveBallDemo/Haptics/BallImpactEventMap.asset`, retaining distinct left/right/body events and the user's existing clip assignments.

Foam appearance: `Assets/GloveBallDemo/Art/Balls/Foam_0.mat`, with procedural porous albedo/normal textures and near-zero smoothness. `Hapbeat > Development > Install Ball Feel Assets` is an explicit authoring command; rerunning preserves existing BallFeelSettings but regenerates the foam material textures. It does not regenerate scenes.

## Audio sources

All five source pages state CC0. Public high-quality previews were decoded to mono 44.1 kHz WAV, trimmed to a single short impact, peak-normalized and edge-faded. They are not lossless original downloads. Exact source URLs, hashes and cut positions are in `Assets/GloveBallDemo/Audio/BallImpacts/sources.json`; reproducible processing is `tools/prepare-ball-impact-audio.py` (Python + FFmpeg). No speaker playback is performed by the script.

1. [Bowling drop/roll/strike — mrrockcandy](https://freesound.org/people/mrrockcandy/sounds/792203/): early ball drop excerpt, not the later pin crash.
2. [Volleyball spike — Luisa_Sanchez](https://freesound.org/people/Luisa_Sanchez/sounds/816991/): contact-microphone recording; timbre differs from an airborne microphone.
3. [Foam Smash — MegaPenguin13](https://freesound.org/people/MegaPenguin13/sounds/118204/): large foam piece, used as a material proxy.
4. [Basketball bounce — toddcircle](https://freesound.org/people/toddcircle/sounds/451642/): basketball bounced on carpet.
5. [Drop (plastic ball) — lori.mortimer](https://freesound.org/people/lori.mortimer/sounds/723791/): processed pickleball dropped in a bathtub.

Final timbre/volume should be auditioned in the HMD. The agent's verification is muted and never sends live haptics.

If a previous editor-authoring run left the Game view muted, select `Hapbeat > Development > Restore Game Audio`. MCP connection and ball asset setup now preserve audio settings. This is an Editor setting, not an APK setting.
