"""Fetch verified CC0 public previews and extract short impacts, without playback."""
import array
import hashlib
import json
from pathlib import Path
import re
import subprocess
import urllib.request
import wave

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'unity/gloveball/Assets/GloveBallDemo/Audio/BallImpacts'
SOURCES = [
    ('Bowling', 'mrrockcandy', 792203, .7, .5),
    ('Volleyball', 'Luisa_Sanchez', 816991, 1.0, .65),
    ('Foam', 'MegaPenguin13', 118204, 5.0, .5),
    ('Basketball', 'toddcircle', 451642, 5.0, .65),
    ('Perforated', 'lori.mortimer', 723791, 3.0, .5),
]

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    records = []
    for name, author, sound_id, search_end, duration in SOURCES:
        url = f'https://freesound.org/people/{author}/sounds/{sound_id}/'
        html = urllib.request.urlopen(url).read().decode()
        if 'creativecommons.org/publicdomain/zero/1.0' not in html:
            raise RuntimeError(f'CC0 license not confirmed: {url}')
        preview = re.search(r'https://cdn\.freesound\.org/previews/[^"\s<>]+-hq\.mp3', html).group()
        data = urllib.request.urlopen(preview).read()
        pcm = subprocess.run(['ffmpeg', '-v', 'error', '-i', 'pipe:0', '-f', 's16le', '-ac', '1', '-ar', '44100', 'pipe:1'], input=data, capture_output=True, check=True).stdout
        samples = array.array('h', pcm)
        # First significant transient, bounded to omit the bowling pin crash.
        block = 441
        energy = [sum(v*v for v in samples[i:i+block])/block for i in range(0, min(len(samples), int(search_end*44100)), block)]
        threshold = max(energy) * .3
        onset = next(i for i, e in enumerate(energy) if e >= threshold)
        start = max(0, onset * block - 220)
        segment = samples[start:start+int(duration*44100)]
        peak = max(abs(v) for v in segment)
        gain = 24000 / max(1, peak)
        for i in range(len(segment)):
            fade = min(1, i/88, (len(segment)-1-i)/882)
            segment[i] = int(segment[i] * gain * max(0, fade))
        path = OUT / f'{name}Impact.wav'
        with wave.open(str(path), 'wb') as output:
            output.setparams((1, 2, 44100, 0, 'NONE', 'not compressed'))
            output.writeframes(segment.tobytes())
        records.append(dict(ball=name, author=author, source=url, license='CC0-1.0', preview=preview,
                            preview_sha256=hashlib.sha256(data).hexdigest(), start_seconds=start/44100,
                            duration_seconds=len(segment)/44100, processing='mono 44.1kHz, trim, peak normalization, edge fades'))
        print(name, 'start', round(start/44100, 3), 'seconds', len(segment)/44100)
    (OUT / 'sources.json').write_text(json.dumps(records, indent=2)+'\n', encoding='utf-8')

if __name__ == '__main__':
    main()
