"""Generate navigation voice WAVs from a voice-lines JSON with a local AivisSpeech / VOICEVOX engine.

Only lines whose text, voice or engine changed are synthesized again, so the script can be
re-run every time a line is edited. Nothing is played back.

  python tools/tts/generate-voice.py <voice-lines.json> <out-dir> [--engine http://127.0.0.1:10101]
  python tools/tts/generate-voice.py --samples <out-dir> [--text "..."]   # one file per speaker style

voice-lines.json:
  {"voice": {"speaker": "まお", "style": "ノーマル", "speed": 1.0, "volume": 0.85},
   "lines": [{"id": "trex_start", "text": "人差し指を上に立てると始まります。"}]}
The shared tools/tts/voice.json (same keys) overrides each file's "voice" so one edit re-voices every demo;
a line may still override "speaker" / "style" / "speed" / "volume" (volumeScale; < 1 leaves peak headroom). Output: <out-dir>/<id>.wav (+ .voice-cache.json).
"""
import argparse
import hashlib
import io
import json
import re
import sys
import urllib.parse
import urllib.request
import wave
from array import array
from pathlib import Path

ID_PATTERN = re.compile(r'^[a-z0-9][a-z0-9_-]{0,63}$')
CACHE_NAME = '.voice-cache.json'
SHARED_VOICE = Path(__file__).with_name('voice.json')


def request(engine, path, params=None, body=None):
    url = engine.rstrip('/') + path + ('?' + urllib.parse.urlencode(params) if params else '')
    data = None if body is None else json.dumps(body).encode('utf-8')
    req = urllib.request.Request(url, data=data, method='POST' if (body is not None or path in ('/audio_query', '/synthesis')) else 'GET',
                                 headers={'Content-Type': 'application/json'} if data else {})
    with urllib.request.urlopen(req, timeout=300) as response:
        return response.read()


def speakers(engine):
    return json.loads(request(engine, '/speakers'))


def resolve_style(all_speakers, speaker, style):
    for entry in all_speakers:
        if entry['name'] != speaker:
            continue
        for candidate in entry['styles']:
            if candidate['name'] == style:
                return candidate['id'], entry.get('speaker_uuid', '')
        names = ', '.join(s['name'] for s in entry['styles'])
        raise SystemExit(f'Style "{style}" not found for {speaker}. Available: {names}')
    names = ', '.join(e['name'] for e in all_speakers)
    raise SystemExit(f'Speaker "{speaker}" not found. Installed: {names}')


def synthesize(engine, style_id, text, speed, volume=1.0):
    query = json.loads(request(engine, '/audio_query', {'speaker': style_id, 'text': text}))
    query['speedScale'] = speed
    query['volumeScale'] = volume
    return request(engine, '/synthesis', {'speaker': style_id}, query)


def check_wav(data, label):
    with wave.open(io.BytesIO(data)) as wav:
        frames = wav.readframes(wav.getnframes())
        seconds = wav.getnframes() / wav.getframerate()
        if wav.getsampwidth() != 2:
            return seconds
    samples = array('h', frames)
    peak = max((abs(s) for s in samples), default=0)
    if seconds < 0.2 or peak < 300:
        raise SystemExit(f'{label}: generated audio looks empty ({seconds:.2f} s, peak {peak}).')
    return seconds


def generate(args):
    spec = json.loads(Path(args.lines).read_text(encoding='utf-8-sig'))
    voice = dict(spec.get('voice', {}))
    if SHARED_VOICE.exists() and not args.ignore_shared:
        voice.update(json.loads(SHARED_VOICE.read_text(encoding='utf-8-sig')))
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    cache_path = out / CACHE_NAME
    cache = json.loads(cache_path.read_text(encoding='utf-8')) if cache_path.exists() else {}
    version = request(args.engine, '/version').decode('utf-8').strip('"')
    installed = speakers(args.engine)
    seen = set()
    made = skipped = 0
    for line in spec['lines']:
        line_id, text = line['id'], line['text'].strip()
        if not ID_PATTERN.match(line_id) or line_id in seen or not text:
            raise SystemExit(f'Invalid or duplicate line: {line_id!r}')
        seen.add(line_id)
        speaker = line.get('speaker', voice.get('speaker'))
        style = line.get('style', voice.get('style', 'ノーマル'))
        speed = float(line.get('speed', voice.get('speed', 1.0)))
        volume = float(line.get('volume', voice.get('volume', 1.0)))
        style_id, speaker_uuid = resolve_style(installed, speaker, style)
        key = hashlib.sha256(json.dumps([version, speaker_uuid, style, speed, volume, text], ensure_ascii=False).encode('utf-8')).hexdigest()
        target = out / f'{line_id}.wav'
        if cache.get(line_id) == key and target.exists() and not args.force:
            skipped += 1
            continue
        data = synthesize(args.engine, style_id, text, speed, volume)
        seconds = check_wav(data, line_id)
        target.write_bytes(data)
        cache[line_id] = key
        made += 1
        print(f'{line_id}: {seconds:.2f} s')
    for stale in sorted(set(cache) - seen):
        cache.pop(stale)
        (out / f'{stale}.wav').unlink(missing_ok=True)
        print(f'{stale}: removed')
    cache_path.write_text(json.dumps(cache, ensure_ascii=False, indent=1, sort_keys=True), encoding='utf-8')
    print(f'generated {made}, unchanged {skipped} (engine {version})')


def samples(args):
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    for entry in speakers(args.engine):
        for style in entry['styles']:
            data = synthesize(args.engine, style['id'], args.text, 1.0)
            seconds = check_wav(data, f"{entry['name']}/{style['name']}")
            name = re.sub(r'[\\/:*?"<>|\s]+', '_', f"{entry['name']}-{style['name']}")
            (out / f'{name}.wav').write_bytes(data)
            print(f"{name}.wav: {seconds:.2f} s")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('lines', nargs='?')
    parser.add_argument('out')
    parser.add_argument('--engine', default='http://127.0.0.1:10101')
    parser.add_argument('--samples', action='store_true', help='write one sample per installed speaker style')
    parser.add_argument('--text', default='こんにちは。目の前のティラノサウルスに、そっと手を伸ばしてみてください。')
    parser.add_argument('--force', action='store_true', help='regenerate every line')
    parser.add_argument('--ignore-shared', action='store_true', help='use only the own "voice" block of the lines file')
    args = parser.parse_args()
    if args.samples:
        samples(args)
    elif args.lines:
        generate(args)
    else:
        parser.error('voice-lines JSON is required unless --samples is given')


if __name__ == '__main__':
    sys.exit(main())
