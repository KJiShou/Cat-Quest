"""Explicit offline generation: python Tools/DialogueAudio/generate.py [--check].
Requires Node and PyYAML. Never opens Unity, edits scenes, or accesses player saves.
"""
import hashlib, json, re, subprocess, sys, uuid
from pathlib import Path
import yaml

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Resources/DialogueAudio'

def lines():
    scene = (ROOT/'Assets/Scenes/IntroStory.unity').read_text(encoding='utf-8-sig')
    objects = {}
    for match in re.finditer(r'^--- !u!\d+ &(\d+)[^\n]*\n(.*?)(?=^--- !u!|\Z)', scene, re.M | re.S):
        objects[int(match[1])] = next(iter(yaml.safe_load(match[2]).values()))
    controller = next(v for v in objects.values() if 'storySteps' in v)
    result = []
    for step in controller['storySteps']:
        if step['type'] != 1: continue
        name = objects[step['character']['fileID']]['displayName']
        assert name in ('Player', 'Master Loke'), name
        result.append(('player' if name == 'Player' else 'mentor', step['dialogueText']))
    tutorial = (ROOT/'Assets/Scripts/UI/TutorialController.cs').read_text(encoding='utf-8-sig')
    # Dialogue lives in ordinary C# string literals; interpolate placeholders offline only.
    for match in re.finditer(r'\$?"((?:\\.|[^"\\])*)"', tutorial):
        value = json.loads('"'+match[1]+'"')
        if len(value) >= 25 and ' ' in value and value[-1] in '.!?' and not value.startswith((' ', '.')):
            result.append(('mentor', value))
    suffix = '. Complete tasks to grow your cat. You can read the guide in Settings > Help.'
    result.extend(('mentor', f'Unlocks at Lv.{level}'+suffix) for level in (2,5))
    result.append(('mentor', '{error} Your tasks are safe. Try again.'))
    return list(dict.fromkeys(result))

items = []
for role,text in lines():
    key = hashlib.sha256((role+'\n'+text).encode()).hexdigest()[:20]
    spoken = re.sub(r'\{[^}]+\}', lambda m: 'apprentice' if m[0]=='{playerName}' else '10' if m[0].startswith('{receipt.') else 'Unable to save.', text)
    items.append(dict(role=role,text=text,spoken=spoken,clip='DialogueAudio/'+role+'-'+key))

if '--check' in sys.argv:
    existing = json.loads((OUT/'catalog.json').read_text())['lines']
    assert [(x['role'],x['text'],x['clip']) for x in existing] == [(x['role'],x['text'],x['clip']) for x in items], 'Dialogue catalog is stale. Run explicit generation.'
    import wave
    for row in existing:
        asset = ROOT/'Assets/Resources'/(row['clip']+'.wav')
        assert hashlib.sha256(asset.read_bytes()).hexdigest()==row['sha256'], asset
        with wave.open(str(asset),'rb') as w:
            assert w.getnchannels()==1 and w.getsampwidth()==2 and w.getframerate()==48000
            assert abs(w.getnframes()/48000-row['duration']) < .001
        assert asset.with_suffix('.wav.meta').exists()
    print(f'PASS: {len(items)} unique current dialogue/role pairs, WAV hashes, durations, format and metas.')
else:
    OUT.mkdir(parents=True,exist_ok=True)
    process = subprocess.run(['node',str(Path(__file__).with_name('render.cjs'))],input=json.dumps(items),text=True,check=True,capture_output=True,cwd=ROOT)
    print(process.stdout)
    # Only create missing metas. Existing GUIDs always survive regeneration.
    targets = [OUT, ROOT/'Assets/Scripts/Settings/DialogueAudioLibrary.cs'] + list(OUT.glob('*.json')) + list(OUT.glob('*.wav'))
    audio_template = (ROOT/'Assets/Audio/SFX/button_click.wav.meta').read_text()
    for target in targets:
        meta = Path(str(target)+'.meta')
        if meta.exists(): continue
        guid = uuid.uuid4().hex
        if target.suffix == '.wav':
            content = re.sub(r'guid: \w+', 'guid: '+guid,audio_template).replace('normalize: 1','normalize: 0')
        else:
            content = 'fileFormatVersion: 2\nguid: '+guid+'\n'+('folderAsset: yes\n' if target.is_dir() else '')
        meta.write_text(content)
