# Navigation voice generation

Generates the demos' navigation voice WAVs with a local [AivisSpeech Engine](https://github.com/Aivis-Project/AivisSpeech-Engine) (free, runs offline after the first start, VOICEVOX-compatible HTTP API on port 10101). Re-run the script whenever a line changes; only changed lines are synthesized again.

```powershell
# 1. Start the engine (keep it running while generating)
& "$env:LOCALAPPDATA\Programs\AivisSpeech-Engine\run.exe" --host 127.0.0.1 --port 10101 --disable_sentry
# 2. Generate
python tools/tts/generate-voice.py <demo>/.../voice-lines.json <demo>/.../Voice
# Audition every installed voice style (writes one WAV per style, nothing is played)
python tools/tts/generate-voice.py --samples <out-dir> --text "..."
```

`voice-lines.json` holds the default voice and the lines (`id` = output file name):

```json
{"voice": {"speaker": "まお", "style": "ノーマル", "speed": 1.0, "volume": 0.85},
 "lines": [{"id": "trex_start", "text": "人差し指を上に立てると始まります。"}]}
```

## Licensing

- The engine software needs no credit. The bundled default voices (まお, コハク) are under the [Aivis Common Model License 1.0](https://github.com/Aivis-Project/ACML/blob/master/ACML-1.0.md): commercial use is allowed and credit is optional. Check the license of any other model before using it.
- Do not use the paid AivisSpeech cloud API; the local engine is free.

## Engine install notes (Windows)

- Install: extract `AivisSpeech-Engine-Windows-x64-<version>.7z.001` from the official GitHub release to `%LOCALAPPDATA%\Programs\AivisSpeech-Engine` (contains `run.exe`).
- The first start downloads two voice models (~250 MB) and the BERT model `tsukumijima/deberta-v2-large-japanese-char-wwm-onnx` (~650 MB) into `%APPDATA%\AivisSpeech-Engine`.
- If Windows symlinks are enabled (Developer Mode), the Hugging Face cache stores the BERT files as symlinks, which onnxruntime cannot load (`NO_SUCHFILE ... model_fp16.onnx`); the engine then deletes the cache and fails. Fix: put real files into `%APPDATA%\AivisSpeech-Engine\BertModelCaches\models--tsukumijima--deberta-v2-large-japanese-char-wwm-onnx\snapshots\<revision>\` (`model_fp16.onnx`, `config.json`, `special_tokens_map.json`, `tokenizer.json`, `tokenizer_config.json`, `vocab.txt`, downloaded from `https://huggingface.co/tsukumijima/deberta-v2-large-japanese-char-wwm-onnx/resolve/<revision>/<file>`; the revision is in the error message) and start the engine again.
