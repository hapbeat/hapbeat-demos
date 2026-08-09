#!/usr/bin/env python3
"""vr-shooter-kit の触覚クリップ (install-clips/*.wav) を合成する。

Python 3.12 標準ライブラリのみ (wave / math / struct / random / json / argparse)。
出力は 16 kHz / mono / 16-bit PCM (contracts specs/kit-format.md §6)。
ノイズ成分は固定シードなので、再実行すると常に同一バイト列が得られる。

使い方:
    python generate_clips.py            # install-clips/ に 9 本を生成
    python generate_clips.py --verify   # 生成物 + manifest.json を機械検証
"""

from __future__ import annotations

import argparse
import json
import math
import os
import random
import re
import struct
import wave

SAMPLE_RATE = 16000
CHANNELS = 1
SAMPLE_WIDTH = 2  # 16-bit PCM

# ピーク目標 -1 dBFS。デバイス側ミキサーのヘッドルームを 1 dB 残す。
TARGET_PEAK = 10 ** (-1.0 / 20.0)  # ≒ 0.891

CLIPS_DIR = "install-clips"
MANIFEST_NAME = "manifest.json"
KIT_NAME = "vr-shooter-kit"

EVENT_ID_RE = re.compile(r"^[a-z][a-z0-9-]*\.[a-z][a-z0-9_-]*$")


# --------------------------------------------------------------------------
# 合成プリミティブ
# --------------------------------------------------------------------------

def n_samples(duration_ms: float) -> int:
    return int(round(SAMPLE_RATE * duration_ms / 1000.0))


def sine(buf: list[float], freq: float, amp: float, tau: float,
         start_ms: float = 0.0, dur_ms: float | None = None) -> None:
    """指数減衰する正弦波を buf に加算する (tau 秒で 1/e)。"""
    start = n_samples(start_ms)
    end = len(buf) if dur_ms is None else min(len(buf), start + n_samples(dur_ms))
    w = 2.0 * math.pi * freq / SAMPLE_RATE
    for i in range(start, end):
        t = (i - start) / SAMPLE_RATE
        buf[i] += amp * math.exp(-t / tau) * math.sin(w * (i - start))


def noise(buf: list[float], rng: random.Random, amp: float, tau: float,
          start_ms: float = 0.0, dur_ms: float | None = None) -> None:
    """指数減衰する白色ノイズを buf に加算する。"""
    start = n_samples(start_ms)
    end = len(buf) if dur_ms is None else min(len(buf), start + n_samples(dur_ms))
    for i in range(start, end):
        t = (i - start) / SAMPLE_RATE
        buf[i] += amp * math.exp(-t / tau) * rng.uniform(-1.0, 1.0)


def one_pole_lowpass(buf: list[float], cutoff_start: float, cutoff_end: float) -> None:
    """カットオフを cutoff_start → cutoff_end へ線形に掃引する 1 次 LPF を in-place 適用。"""
    y = 0.0
    last = max(1, len(buf) - 1)
    for i, x in enumerate(buf):
        fc = cutoff_start + (cutoff_end - cutoff_start) * (i / last)
        a = 1.0 - math.exp(-2.0 * math.pi * fc / SAMPLE_RATE)
        y += a * (x - y)
        buf[i] = y


def apply_edges(buf: list[float], attack_ms: float = 2.0, release_ms: float = 4.0) -> None:
    """両端に短いフェードを掛けてクリックノイズ (不連続) を避ける。"""
    a = min(n_samples(attack_ms), len(buf) // 2)
    r = min(n_samples(release_ms), len(buf) // 2)
    for i in range(a):
        buf[i] *= i / a
    for i in range(r):
        buf[len(buf) - 1 - i] *= i / r


def normalize(buf: list[float], peak: float = TARGET_PEAK) -> None:
    """ピークを目標値に揃える (全クリップの体感レンジを manifest の intensity 側に寄せる)。"""
    current = max(abs(v) for v in buf)
    if current <= 0.0:
        raise ValueError("silent buffer")
    scale = peak / current
    for i in range(len(buf)):
        buf[i] *= scale


def write_wav(path: str, buf: list[float]) -> None:
    data = struct.pack("<%dh" % len(buf),
                       *[max(-32768, min(32767, int(round(v * 32767.0)))) for v in buf])
    with wave.open(path, "wb") as w:
        w.setnchannels(CHANNELS)
        w.setsampwidth(SAMPLE_WIDTH)
        w.setframerate(SAMPLE_RATE)
        w.writeframes(data)


# --------------------------------------------------------------------------
# クリップ定義 (設計書 dev-notes/demos/design-vr-mod-demos-202608051200.md のクリップ表)
# --------------------------------------------------------------------------

def make_shot_recoil(rng: random.Random) -> list[float]:
    """発砲リコイル: 減衰ノイズ + 60 Hz パンチ。"""
    buf = [0.0] * n_samples(80)
    noise(buf, rng, amp=0.55, tau=0.012)
    sine(buf, freq=60.0, amp=1.0, tau=0.030)
    sine(buf, freq=120.0, amp=0.25, tau=0.014)
    apply_edges(buf, 1.0, 6.0)
    return buf


def make_hit_light(rng: random.Random) -> list[float]:
    """軽被弾 / 敵ヒット: 100 Hz 短バースト。"""
    buf = [0.0] * n_samples(60)
    sine(buf, freq=100.0, amp=1.0, tau=0.018)
    noise(buf, rng, amp=0.12, tau=0.005)
    apply_edges(buf, 1.0, 5.0)
    return buf


def make_hit_heavy(rng: random.Random) -> list[float]:
    """重被弾: 50 Hz 強バースト + 減衰。"""
    buf = [0.0] * n_samples(250)
    sine(buf, freq=50.0, amp=1.0, tau=0.070)
    sine(buf, freq=100.0, amp=0.28, tau=0.035)
    noise(buf, rng, amp=0.20, tau=0.010)
    apply_edges(buf, 1.5, 10.0)
    return buf


def make_reload_click(rng: random.Random) -> list[float]:
    """リロード: 高域の短音を 2 連。"""
    buf = [0.0] * n_samples(120)
    for start_ms, amp in ((0.0, 0.75), (60.0, 1.0)):
        sine(buf, freq=900.0, amp=amp, tau=0.004, start_ms=start_ms, dur_ms=40.0)
        sine(buf, freq=1400.0, amp=amp * 0.5, tau=0.003, start_ms=start_ms, dur_ms=40.0)
        noise(buf, rng, amp=amp * 0.3, tau=0.002, start_ms=start_ms, dur_ms=40.0)
    apply_edges(buf, 0.5, 5.0)
    return buf


def make_heartbeat(rng: random.Random) -> list[float]:
    """低体力ループ: ドクン (lub-dub) 2 拍 + 無音の間。loop=true 前提で端は無音。"""
    buf = [0.0] * n_samples(700)
    # lub — 強く短い
    sine(buf, freq=52.0, amp=1.0, tau=0.055, start_ms=10.0, dur_ms=200.0)
    # dub — やや弱く長い
    sine(buf, freq=44.0, amp=0.72, tau=0.075, start_ms=210.0, dur_ms=250.0)
    apply_edges(buf, 2.0, 8.0)
    return buf


def make_beat_pulse(rng: random.Random) -> list[float]:
    """BGM 拍: 80 Hz ワンショット。"""
    buf = [0.0] * n_samples(50)
    sine(buf, freq=80.0, amp=1.0, tau=0.012)
    apply_edges(buf, 1.0, 5.0)
    return buf


def make_kill_confirm(rng: random.Random) -> list[float]:
    """敵撃破: 上昇 2 音。"""
    buf = [0.0] * n_samples(150)
    sine(buf, freq=120.0, amp=0.8, tau=0.025, start_ms=0.0, dur_ms=70.0)
    sine(buf, freq=180.0, amp=1.0, tau=0.030, start_ms=75.0, dur_ms=75.0)
    apply_edges(buf, 1.5, 8.0)
    return buf


def make_slash(rng: random.Random) -> list[float]:
    """近接ヒット (B&S): ざらつきノイズの sweep。"""
    length = n_samples(120)
    buf = [0.0] * length
    noise(buf, rng, amp=1.0, tau=0.045)
    one_pole_lowpass(buf, cutoff_start=2500.0, cutoff_end=180.0)
    # ざらつき: 180 Hz の振幅変調
    for i in range(length):
        t = i / SAMPLE_RATE
        buf[i] *= 0.75 + 0.25 * math.sin(2.0 * math.pi * 180.0 * t)
    sine(buf, freq=70.0, amp=0.25, tau=0.040)
    apply_edges(buf, 3.0, 8.0)
    return buf


def make_block_thud(rng: random.Random) -> list[float]:
    """ガード / パリィ: 低域のドン。"""
    buf = [0.0] * n_samples(100)
    sine(buf, freq=45.0, amp=1.0, tau=0.030)
    noise(buf, rng, amp=0.25, tau=0.006)
    apply_edges(buf, 1.0, 8.0)
    return buf


# (file basename, duration_ms, synth, noise seed)
CLIP_SPECS = [
    ("shot_recoil", 80, make_shot_recoil, 1001),
    ("hit_light", 60, make_hit_light, 1002),
    ("hit_heavy", 250, make_hit_heavy, 1003),
    ("reload_click", 120, make_reload_click, 1004),
    ("heartbeat", 700, make_heartbeat, 1005),
    ("beat_pulse", 50, make_beat_pulse, 1006),
    ("kill_confirm", 150, make_kill_confirm, 1007),
    ("slash", 120, make_slash, 1008),
    ("block_thud", 100, make_block_thud, 1009),
]


# --------------------------------------------------------------------------
# 生成 / 検証
# --------------------------------------------------------------------------

def generate(out_dir: str) -> None:
    os.makedirs(out_dir, exist_ok=True)
    for name, duration_ms, synth, seed in CLIP_SPECS:
        buf = synth(random.Random(seed))
        normalize(buf)
        path = os.path.join(out_dir, name + ".wav")
        write_wav(path, buf)
        print(f"wrote {path} ({duration_ms} ms, {len(buf)} samples)")


def verify(kit_dir: str) -> bool:
    """WAV フォーマット / 長さ / ピークと、manifest の整合を機械検証する。"""
    ok = True
    clips_dir = os.path.join(kit_dir, CLIPS_DIR)

    for name, duration_ms, _synth, _seed in CLIP_SPECS:
        path = os.path.join(clips_dir, name + ".wav")
        if not os.path.isfile(path):
            print(f"FAIL {name}: missing {path}")
            ok = False
            continue
        with wave.open(path, "rb") as w:
            rate, ch, width, frames = (w.getframerate(), w.getnchannels(),
                                       w.getsampwidth(), w.getnframes())
            raw = w.readframes(frames)
        problems = []
        if rate != SAMPLE_RATE:
            problems.append(f"sample_rate={rate} (want {SAMPLE_RATE})")
        if ch != CHANNELS:
            problems.append(f"channels={ch} (want {CHANNELS})")
        if width != SAMPLE_WIDTH:
            problems.append(f"sample_width={width * 8}bit (want 16bit)")
        actual_ms = frames * 1000.0 / rate if rate else 0.0
        if abs(actual_ms - duration_ms) > duration_ms * 0.10:
            problems.append(f"duration={actual_ms:.1f}ms (want {duration_ms}±10%)")
        samples = struct.unpack("<%dh" % (len(raw) // 2), raw)
        peak = max(abs(s) for s in samples) / 32767.0 if samples else 0.0
        if not (0.5 <= peak <= 0.98):
            problems.append(f"peak={peak:.3f} (want 0.5..0.98)")
        if problems:
            print(f"FAIL {name}: " + "; ".join(problems))
            ok = False
        else:
            print(f"PASS {name}: {actual_ms:.1f}ms, {rate}Hz, {ch}ch, "
                  f"{width * 8}bit, peak={peak:.3f}")

    # manifest
    manifest_path = os.path.join(kit_dir, MANIFEST_NAME)
    if not os.path.isfile(manifest_path):
        print(f"FAIL manifest: missing {manifest_path}")
        return False
    with open(manifest_path, encoding="utf-8") as f:
        manifest = json.load(f)

    if manifest.get("name") != KIT_NAME:
        print(f"FAIL manifest: name={manifest.get('name')!r} (want {KIT_NAME!r})")
        ok = False
    if manifest.get("schema_version") != "2.0.0":
        print(f"FAIL manifest: schema_version={manifest.get('schema_version')!r}")
        ok = False

    events = manifest.get("events", {})
    expected_ids = {f"{KIT_NAME}.{name}" for name, *_ in CLIP_SPECS}
    if set(events) != expected_ids:
        print(f"FAIL manifest: events keys mismatch "
              f"(missing={sorted(expected_ids - set(events))}, "
              f"extra={sorted(set(events) - expected_ids)})")
        ok = False
    for event_id, ev in sorted(events.items()):
        problems = []
        if not EVENT_ID_RE.match(event_id):
            problems.append("event id does not match ^[a-z][a-z0-9-]*\\.[a-z][a-z0-9_-]*$")
        clip = ev.get("clip")
        if not clip:
            problems.append("missing clip")
        elif not os.path.isfile(os.path.join(clips_dir, clip)):
            problems.append(f"clip file not found: {CLIPS_DIR}/{clip}")
        elif clip != event_id.split(".", 1)[1] + ".wav":
            problems.append(f"clip {clip!r} does not match event id basename")
        intensity = ev.get("parameters", {}).get("intensity")
        if intensity is not None and not (0.0 <= intensity <= 1.0):
            problems.append(f"intensity={intensity} out of 0..1")
        if problems:
            print(f"FAIL {event_id}: " + "; ".join(problems))
            ok = False
        else:
            print(f"PASS {event_id}: clip={clip}, intensity={intensity}, "
                  f"loop={ev.get('parameters', {}).get('loop', False)}")

    install_clips = manifest.get("install_clips", {})
    for name, duration_ms, _synth, _seed in CLIP_SPECS:
        meta = install_clips.get(name + ".wav")
        if meta is None:
            print(f"FAIL install_clips: missing entry for {name}.wav")
            ok = False
            continue
        problems = []
        if meta.get("duration_ms") != duration_ms:
            problems.append(f"duration_ms={meta.get('duration_ms')} (want {duration_ms})")
        if meta.get("sample_rate") != SAMPLE_RATE:
            problems.append(f"sample_rate={meta.get('sample_rate')}")
        if meta.get("channels") != CHANNELS:
            problems.append(f"channels={meta.get('channels')}")
        if meta.get("format") != "pcm_s16le":
            problems.append(f"format={meta.get('format')!r}")
        if problems:
            print(f"FAIL install_clips[{name}.wav]: " + "; ".join(problems))
            ok = False

    print("\n" + ("ALL CHECKS PASSED" if ok else "SOME CHECKS FAILED"))
    return ok


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify", action="store_true",
                        help="生成せず、既存の WAV と manifest.json を検証する")
    args = parser.parse_args()

    kit_dir = os.path.dirname(os.path.abspath(__file__))
    if args.verify:
        return 0 if verify(kit_dir) else 1
    generate(os.path.join(kit_dir, CLIPS_DIR))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
