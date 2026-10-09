"""Normalize narration files to the demos' common loudness with ffmpeg (no engine needed). Nothing is played.

hapbeat-contracts specs/demo-session.md "ナレーションの音量": every narration file is normalized to an integrated
loudness of -20 LUFS (ITU-R BS.1770) with a true peak at most -1.5 dBTP, so one `narration_volume` sounds the same
in every demo. generate-voice.py runs this on each line it synthesizes; this script also works on ready-made files.

  python tools/tts/loudness.py <file>...            # normalize in place (WAV stays 16-bit PCM, OGG stays Vorbis)
  python tools/tts/loudness.py --check <file>...    # measure only (ebur128); exit 1 when one is off the target

ffmpeg: --ffmpeg, else the FFMPEG environment variable, else `ffmpeg` on PATH.
"""
import argparse
import json
import os
import re
import subprocess
import sys
from pathlib import Path

TARGET_LUFS = -20.0
TRUE_PEAK_DBTP = -1.5
# Above any narration line's loudness range, so loudnorm stays linear (one gain for the whole file).
LOUDNESS_RANGE = 20.0
TOLERANCE_LU = 0.5
# The written file is measured again; pass 2 is re-aimed until the result is this close to the target.
ACCEPT_LU = 0.1
RETRIES = 3
# Written into generate-voice.py's cache: a change of the target regenerates the lines.
LOUDNORM_TAG = f'loudnorm I={TARGET_LUFS:g} TP={TRUE_PEAK_DBTP:g}'
CODECS = {'.wav': ['-c:a', 'pcm_s16le'], '.ogg': ['-c:a', 'libvorbis', '-q:a', '6']}


def default_ffmpeg():
    return os.environ.get('FFMPEG', 'ffmpeg')


def run(ffmpeg, args):
    result = subprocess.run([ffmpeg, '-hide_banner', '-nostdin', '-nostats', *args],
                            capture_output=True, text=True, encoding='utf-8', errors='replace')
    if result.returncode != 0:
        raise SystemExit(f'ffmpeg failed ({result.returncode}): {" ".join(args)}\n{result.stderr[-2000:]}')
    return result.stderr


def last_json(stderr):
    start = stderr.rfind('{')
    end = stderr.rfind('}')
    if start < 0 or end < start:
        raise SystemExit('loudnorm printed no measurement')
    return json.loads(stderr[start:end + 1])


def stream_info(ffmpeg, path):
    """Sample rate and channel count of the first audio stream (from ffmpeg's input banner)."""
    stderr = subprocess.run([ffmpeg, '-hide_banner', '-nostdin', '-i', str(path)],
                            capture_output=True, text=True, encoding='utf-8', errors='replace').stderr
    match = re.search(r'Audio: .*?, (\d+) Hz, (mono|stereo|(\d+) channels)', stderr)
    if not match:
        raise SystemExit(f'{path}: no audio stream found')
    channels = 1 if match.group(2) == 'mono' else 2 if match.group(2) == 'stereo' else int(match.group(3))
    return int(match.group(1)), channels


def ebur128(path, ffmpeg=None):
    """ffmpeg's ebur128 summary: integrated loudness I (LUFS), its gate threshold (LUFS), LRA (LU), true peak TP (dBTP)."""
    stderr = run(ffmpeg or default_ffmpeg(), ['-i', str(path), '-af', 'ebur128=peak=true', '-f', 'null', '-'])
    summary = stderr[stderr.rfind('Summary:'):]
    number = r'(-?[\d.]+|-inf)'
    found = {
        'I': re.search(r'I:\s+' + number + r' LUFS\s+Threshold:\s+' + number + ' LUFS', summary),
        'LRA': re.search(r'LRA:\s+' + number + r' LU\b', summary),
        'TP': re.search(r'True peak:\s+Peak:\s+' + number + ' dBFS', summary),
    }
    if not all(found.values()):
        raise SystemExit(f'{path}: ebur128 printed no summary')
    return {'I': float(found['I'].group(1)), 'thresh': float(found['I'].group(2)),
            'LRA': float(found['LRA'].group(1)), 'TP': float(found['TP'].group(1))}


def measure(path, ffmpeg=None):
    """Integrated loudness (LUFS) and true peak (dBTP)."""
    result = ebur128(path, ffmpeg)
    return result['I'], result['TP']


def normalize(path, ffmpeg=None):
    """
    Two-pass loudnorm of `path` in place (same sample rate, channels and codec). Returns the pass-2 report.
    Pass 1 measures with ebur128, not with loudnorm's own first pass: on these short lines loudnorm's first-pass
    input_i differs from BS.1770 by up to ~0.8 LU, so its linear gain misses the target by as much.
    The written file is measured again, and pass 2 runs again from the original with the target corrected by the
    miss (at most RETRIES times) when it is off by more than ACCEPT_LU. Short lines miss in two cases: when one
    gain cannot reach the target without the true peak going over, loudnorm limits the peaks instead (dynamic
    mode), whose gain misses on short lines; and a Vorbis round trip moves a ~1 s clip by up to 0.5 LU.
    """
    ffmpeg = ffmpeg or default_ffmpeg()
    path = Path(path)
    codec = CODECS.get(path.suffix.lower())
    if codec is None:
        raise SystemExit(f'{path}: only {", ".join(CODECS)} are supported')
    rate, channels = stream_info(ffmpeg, path)
    first = ebur128(path, ffmpeg)
    temp = path.with_name(path.stem + '.loudnorm' + path.suffix)
    target = TARGET_LUFS
    try:
        for _ in range(1 + RETRIES):
            second_filter = (f'loudnorm=I={target:.2f}:TP={TRUE_PEAK_DBTP}:LRA={LOUDNESS_RANGE}'
                             f':measured_I={first["I"]}:measured_TP={first["TP"]}:measured_LRA={first["LRA"]}'
                             f':measured_thresh={first["thresh"]}:linear=true:print_format=json')
            # loudnorm works at 192 kHz: resample back to the file's own rate. Bit-exact: no encoder tag in the file.
            stderr = run(ffmpeg, ['-y', '-i', str(path), '-af', second_filter, '-ar', str(rate), '-ac', str(channels),
                                  *codec, '-map_metadata', '-1', '-fflags', '+bitexact', '-flags:a', '+bitexact', str(temp)])
            report = last_json(stderr)
            miss = TARGET_LUFS - ebur128(temp, ffmpeg)['I']
            if abs(miss) <= ACCEPT_LU:
                break
            target += miss
        temp.replace(path)
    finally:
        temp.unlink(missing_ok=True)
    if report.get('normalization_type') != 'linear':
        print(f'{path.name}: peaks limited (loudnorm {report.get("normalization_type")} mode)', file=sys.stderr)
    return report


def within_target(loudness):
    return abs(loudness - TARGET_LUFS) <= TOLERANCE_LU


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('files', nargs='+')
    parser.add_argument('--check', action='store_true', help='measure only, do not change the files')
    parser.add_argument('--ffmpeg', default=default_ffmpeg())
    args = parser.parse_args()
    off = 0
    for name in args.files:
        before = measure(name, args.ffmpeg)
        if not args.check:
            normalize(name, args.ffmpeg)
        loudness, peak = measure(name, args.ffmpeg) if not args.check else before
        ok = within_target(loudness) and peak <= TRUE_PEAK_DBTP
        off += not ok
        change = '' if args.check else f'{before[0]:.1f} LUFS -> '
        print(f'{Path(name).name}: {change}{loudness:.1f} LUFS, true peak {peak:.1f} dBTP{"" if ok else "  OFF TARGET"}')
    return 1 if off else 0


if __name__ == '__main__':
    sys.exit(main())
