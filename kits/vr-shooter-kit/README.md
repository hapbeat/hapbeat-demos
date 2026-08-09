# vr-shooter-kit

VR mod デモ（Pistol Whip / Robo Recall / Blade & Sorcery）で共通に使う触覚 Kit。
16 kHz / mono / 16-bit PCM の合成クリップ 9 本を収録する。

## 収録クリップ

| Event ID | ファイル | 用途 | 長さ | intensity |
|---|---|---|---|---|
| `vr-shooter-kit.shot_recoil` | `shot_recoil.wav` | 発砲リコイル（減衰ノイズ + 60 Hz パンチ） | 80 ms | 1.0 |
| `vr-shooter-kit.hit_light` | `hit_light.wav` | 軽被弾 / 敵ヒット（100 Hz 短バースト） | 60 ms | 0.8 |
| `vr-shooter-kit.hit_heavy` | `hit_heavy.wav` | 重被弾（50 Hz 強バースト + 減衰） | 250 ms | 1.0 |
| `vr-shooter-kit.reload_click` | `reload_click.wav` | リロード（高域の短音 2 連） | 120 ms | 0.8 |
| `vr-shooter-kit.heartbeat` | `heartbeat.wav` | 低体力ループ（ドクン 2 拍、`loop=true`） | 700 ms | 0.9 |
| `vr-shooter-kit.beat_pulse` | `beat_pulse.wav` | BGM 拍（80 Hz ワンショット） | 50 ms | 0.85 |
| `vr-shooter-kit.kill_confirm` | `kill_confirm.wav` | 敵撃破（上昇 2 音） | 150 ms | 0.85 |
| `vr-shooter-kit.slash` | `slash.wav` | 近接ヒット（ざらつきノイズ sweep） | 120 ms | 0.9 |
| `vr-shooter-kit.block_thud` | `block_thud.wav` | ガード / パリィ（低域のドン） | 100 ms | 0.95 |

全クリップともピークは -1 dBFS に揃えてある。相対的な強さは manifest の `intensity` と
mod 設定（`hapbeat_settings.json` の `gain`）で調整する。

## デバイスへの導入

1. [Hapbeat Studio](https://studio.hapbeat.com/) を開き、Helper を起動してデバイスを認識させる
2. Kit の作業フォルダとして、この `vr-shooter-kit` の**親ディレクトリ**（`kits/`）を選択する
3. Kit 一覧に `vr-shooter-kit` が現れるので、対象デバイスを選んで **Deploy** する

詳しい手順はドキュメントポータル（https://devtools.hapbeat.com/）の Studio ガイドを参照。

## クリップの再生成

```
python generate_clips.py            # install-clips/*.wav を再生成
python generate_clips.py --verify   # WAV フォーマット / 長さ / ピークと manifest の整合を検証
```

Python 3.12 の標準ライブラリのみで動作する（外部パッケージ不要）。ノイズ成分はシード固定なので、
再実行しても同一のバイト列が得られる。生成済み WAV も commit してあるため、通常は実行不要。
