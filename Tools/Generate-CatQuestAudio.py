"""Original Cat Quest synthesis. Python + numpy; no samples, network or paid API.

Run from project root. Existing outputs are refused unless --replace is supplied.
This authors WAV files only; it never edits Unity assets, GUIDs or player settings.
"""
import argparse
import hashlib
import json
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SR = 48000
RNG = np.random.default_rng(20261025)
SPECS = [
    ("musicClip", "Music/menu_garden", 80, "温暖猫咪花园", "Warm, playful instrumental for a cozy cat productivity game. 80 BPM, C major, 16 bars. Soft rounded kalimba-like plucks, mellow electric-piano chords, warm bass and tiny brushed percussion. A gentle original four-note motif with sparse variations. Light, welcoming, never busy. No vocals, animal voices or abrupt accents. Exact musical loop with ambience wrapping across the seam."),
    ("focusMusic", "Music/focus_clouds", 64, "安静专注云朵", "Quiet concentration instrumental. 64 BPM, C major/A minor colour, 16 bars. Slow soft sine-based pad chords, sparse rounded electric-piano notes, very gentle bass. No drums, no bright lead, no ticking, no vocals. More space and lower level than menu music; steady and unobtrusive for long work sessions. Exact musical loop, no dramatic build or ending."),
    ("restMusic", "Music/rest_sunbeam", 72, "阳光下休息", "Restful, reassuring instrumental for a short break. 72 BPM, C major, 16 bars. Warm sustained chords, occasional soft bell and relaxed plucked arpeggios over gentle bass. Airy stereo space, no urgent rhythm, no vocals or nature recordings. A simple uplifting melody related to the menu theme. Exact musical loop with no fade-to-silence between repetitions."),
    ("battleMusic", "Music/battle_paws", 120, "勇敢猫爪战斗", "Friendly fantasy cat boss-battle instrumental. 120 BPM, A minor, 16 bars. Rounded synth-pluck ostinato, soft electronic kick, restrained noise snare and hats, warm pulsing bass, short original heroic melody. Energetic but cute, not horror or aggressive metal. Keep room for impact sounds. No vocals, no sampled orchestra, no explosive transition. Exact musical loop."),
    ("buttonClick", "SFX/button_click", .14, "柔软按钮点击", "One soft tactile UI pop, 0.14 seconds. Rounded wooden/plastic-like synthesized tap with a tiny descending pitch bend and a dry delicate transient. Immediate response, quiet and pleasant when repeated. No metallic ringing, double click, hiss bed or reverb."),
    ("previewClip", "SFX/settings_preview", .65, "设置音量预览", "One gentle two-note synthetic marimba preview, 0.65 seconds. Mid-register C then E, evenly balanced and clearly audible for setting sound volume. Rounded attack, short clean decay, no loud bass or sharp treble. Distinct from a success fanfare."),
    ("taskComplete", "SFX/task_complete", 1.05, "任务完成", "A warm three-note ascending success chime, 1.05 seconds, C-E-G. Soft bell/pluck blend, subtle sparkle and a compact pleasant tail. Small everyday achievement, not a grand victory. No coin cascade, speech or long reverb."),
    ("levelUp", "SFX/level_up", 1.8, "猫咪升级", "A cheerful original rising level-up jingle, 1.8 seconds, C-E-G-high C, with a soft major chord underneath the final note. Rounded digital bell, slightly brighter than task completion, compact shimmering tail. Cute and rewarding, no piercing arcade siren or vocals."),
    ("catAttack", "SFX/cat_attack", .24, "猫咪刀剑命中", "Single small fantasy sword swish and compact impact, 0.24 seconds. Quick filtered-air sweep into a light woody-metallic tick, low-volume low-mid impact body. Agile, playful, readable during repeated attacks. No scream, gore, long ringing blade or background ambience."),
    ("bossAttack", "SFX/boss_attack", .36, "Boss 重击", "Single stylized boss impact, 0.36 seconds. Short descending low-mid thump with a muted crunchy noise transient and a very small air rush. Heavier than the cat attack but comfortable at desktop volume. No sub-bass explosion, voice, gore or long rumble."),
    ("battleVictory", "SFX/battle_victory", 2.6, "战斗胜利", "Original short friendly victory fanfare, 2.6 seconds. Rising C-E-G, a brief pause, then bright high C over a warm C-major chord. Rounded synth bells and gentle bass, celebratory but restrained, clean final decay. No orchestra samples, crowd, vocals or excessive loudness."),
    ("battleDefeat", "SFX/battle_defeat", 1.8, "温和战败提示", "Kind, non-punishing defeat cue, 1.8 seconds. Three soft descending notes E-C-A with a mellow minor-colour pad and a warm fading low note. Encourages trying again rather than alarming the user. No buzzer, harsh dissonance, voice or dramatic crash."),
    ("focusComplete", "SFX/focus_complete", 1.6, "专注完成铃声", "A clear calm two-note completion bell, 1.6 seconds. C then G, soft mallet attack and restrained airy harmonics, enough sustain to notice without startling. Distinct from task reward and from rest completion. No alarm loop, ticking, speech or harsh high-frequency ping."),
    ("restComplete", "SFX/rest_complete", 1.3, "休息结束提示", "A gentle return-to-work cue, 1.3 seconds. Three rounded ascending mallet notes G-A-high C, a little more rhythmic than the focus-complete bell. Friendly invitation, clean soft tail, moderate volume. No alarm, urgency, voice or repeated beeping."),
]


def hz(note):
    return 440 * 2 ** ((note - 69) / 12)


def tone(note, duration, kind="pluck"):
    t = np.arange(round(duration * SR)) / SR
    f = hz(note)
    if kind == "pad":
        y = (np.sin(2*np.pi*f*t) + .19*np.sin(2*np.pi*f*2*t)
             + .12*np.sin(2*np.pi*f*1.003*t)) / 1.31
        env = np.minimum(t/.16, 1) * np.minimum((duration-t)/.32, 1)
    elif kind == "bass":
        y = np.sin(2*np.pi*f*t) + .16*np.sin(2*np.pi*f*2*t)
        env = (1-np.exp(-t*100)) * np.exp(-t*2.4) * np.minimum((duration-t)/.05, 1)
    else:
        # Harmonic, mellow electric mallet; no imported samples.
        y = np.sin(2*np.pi*f*t) + .25*np.sin(2*np.pi*f*2*t)*np.exp(-t*7)
        y += .08*np.sin(2*np.pi*f*3*t)*np.exp(-t*12)
        env = (1-np.exp(-t*450))*np.exp(-t*(3.5 if kind == "bell" else 6))
        env *= np.minimum((duration-t)/.04, 1)
    return y * np.maximum(0, env)


def noise(duration, decay=30):
    n = round(duration*SR)
    white = RNG.normal(0, 1, n)
    # Moving-average lowpass gives soft air/brush rather than sharp white hiss.
    soft = np.convolve(white, np.ones(13)/13, mode="same")
    t = np.arange(n)/SR
    return soft * (1-np.exp(-t*900)) * np.exp(-t*decay) * np.minimum((duration-t)/.015, 1)


def thump(duration, high=125, low=55):
    t = np.arange(round(duration*SR))/SR
    phase = 2*np.pi*(low*t+(high-low)*.025*(1-np.exp(-t/.025)))
    return np.sin(phase)*(1-np.exp(-t*900))*np.exp(-t*17)*np.minimum((duration-t)/.02, 1)


def add(buf, y, when, gain=1, pan=0, wrap=False):
    start = round(when*SR)
    weights = np.array([np.sqrt((1-pan)/2), np.sqrt((1+pan)/2)])
    if wrap:
        idx = (start+np.arange(len(y))) % len(buf)
        np.add.at(buf, idx, y[:, None]*weights*gain)
    elif start < len(buf):
        count = min(len(y), len(buf)-start)
        buf[start:start+count] += y[:count, None]*weights*gain


def music(field, bpm):
    beat = 60/bpm
    buf = np.zeros((round(64*beat*SR), 2), dtype=np.float64)
    battle = field == "battleMusic"
    focus = field == "focusMusic"
    rest = field == "restMusic"
    chords = [[57,60,64,67],[53,57,60,64],[48,52,55,59],[55,59,62,65]] if battle else [
        [60,64,67,71],[57,60,64,67],[53,57,60,64],[55,59,62,69]]
    melody = [[72,76,79,76],[72,71,69,76],[69,72,76,72],[71,74,79,74]]
    if battle:
        melody = [[69,72,76,79],[69,72,77,76],[67,72,76,74],[71,74,79,77]]
    for bar in range(16):
        chord = chords[bar % 4]
        start = bar*4*beat
        for j, n in enumerate(chord):
            add(buf,tone(n-12,4.5*beat,"pad"),start,.055 if focus else .045,(j-1.5)*.3,True)
        for b in ([0] if focus or rest else [0,2]):
            add(buf,tone(chord[0]-24,1.9*beat,"bass"),start+b*beat,.10 if focus else .18,0,True)
        if focus:
            if bar % 2 == 0:
                add(buf,tone(chord[2]+12,2.8,"bell"),start+1.5*beat,.065,.3,True)
        else:
            steps = 8 if battle else 4
            for j in range(steps):
                n = chord[j % 4]+(12 if j % 3 == 2 else 0)
                add(buf,tone(n,.9),start+j*(4/steps)*beat,.10 if rest else .13,(-1 if j%2 else 1)*.32,True)
            if bar % 4 != 3 or battle:
                for j,n in enumerate(melody[bar%4]):
                    # Second half varies register and leaves a breath in every phrase.
                    if rest and j % 2: continue
                    if j == 3 and bar%2: continue
                    add(buf,tone(n+(0 if bar<8 else -12),1.3,"bell"),start+(j+.25)*beat,.10 if rest else .15,-.12,True)
        if battle or field == "musicClip":
            for b in [0,2]:
                add(buf,thump(.25),start+b*beat,.17 if battle else .055,0,True)
            for b in [1,3]:
                add(buf,noise(.18,28),start+b*beat,.22 if battle else .045,.12,True)
            for b in np.arange(.5,4,1 if not battle else .5):
                add(buf,noise(.07,50),start+b*beat,.06 if battle else .028,-.35,True)
    # Periodic echo wraps tails into the start: never truncate a sustained note at the loop.
    dry = buf.copy()
    buf += .13*np.roll(dry,round(beat*.75*SR),axis=0)[:,::-1]
    buf += .07*np.roll(dry,round(beat*1.5*SR),axis=0)
    return buf


def effect(field, duration):
    buf = np.zeros((round(duration*SR),2), dtype=np.float64)
    sequences = {
        "previewClip": [(0,72),(.19,76)],
        "taskComplete": [(0,72),(.15,76),(.3,79)],
        "levelUp": [(0,72),(.16,76),(.32,79),(.52,84)],
        "battleVictory": [(0,72),(.18,76),(.36,79),(.8,84)],
        "battleDefeat": [(0,64),(.28,60),(.57,57)],
        "focusComplete": [(0,72),(.42,79)],
        "restComplete": [(0,67),(.23,69),(.46,72)],
    }
    if field == "buttonClick":
        add(buf,thump(duration,900,390),0,.35)
        add(buf,noise(.045,90),0,.10)
    elif field == "catAttack":
        add(buf,noise(.18,19),0,.48)
        add(buf,thump(.15,540,130),.07,.35)
        add(buf,tone(88,.11),.075,.10)
    elif field == "bossAttack":
        add(buf,thump(.32,190,65),.02,.6)
        add(buf,noise(.23,22),.02,.5)
    else:
        for t,n in sequences[field]:
            add(buf,tone(n,min(duration-t,1.3),"bell"),t,.35)
        if field in ("levelUp","battleVictory","battleDefeat"):
            t = .52 if field == "levelUp" else .8 if field == "battleVictory" else .57
            notes = [60,64,67] if field != "battleDefeat" else [45,52,57]
            for j,n in enumerate(notes):
                add(buf,tone(n,duration-t,"pad"),t,.10,(j-1)*.2)
        dry = buf.copy()
        delay = round(.085*SR)
        buf[delay:] += .08*dry[:-delay,::-1]
    # No DC or clicks at one-shot boundaries.
    buf -= buf.mean(axis=0)
    fade = min(round(.025*SR),len(buf)//4)
    buf[:120] *= np.linspace(0,1,120)[:,None]
    buf[-fade:] *= np.linspace(1,0,fade)[:,None]
    return buf


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--replace",action="store_true")
    args = parser.parse_args()
    outputs = [ROOT/"Assets/Audio"/(s[1]+".wav") for s in SPECS]
    if not args.replace and any(p.exists() for p in outputs):
        raise SystemExit("Audio already exists. Review edits before deliberately using --replace.")
    rows=[]
    for spec,path in zip(SPECS,outputs):
        field, name, value, title, prompt = spec
        is_music = name.startswith("Music/")
        y = music(field,value) if is_music else effect(field,value)
        if is_music: y -= y.mean(axis=0)
        target = .105 if is_music else .38
        if field == "focusMusic": target = .065
        if field == "restMusic": target = .085
        if field in ("buttonClick","catAttack","bossAttack"): target = .26
        y *= target/max(np.max(np.abs(y)),1e-10)
        pcm = np.round(y*32767).astype("<i2")
        assert np.isfinite(y).all() and np.max(np.abs(y)) < 1
        seam = float(np.max(np.abs(y[0]-y[-1])))
        if is_music: assert seam < .01, (field,seam)
        path.parent.mkdir(parents=True,exist_ok=True)
        with wave.open(str(path),"wb") as f:
            f.setnchannels(2);f.setsampwidth(2);f.setframerate(SR);f.writeframes(pcm.tobytes())
        rows.append(dict(field=field,path=path.relative_to(ROOT).as_posix(),title=title,
                         prompt=prompt,seconds=round(len(y)/SR,3),bpm=value if is_music else None,
                         peak_dbfs=round(20*np.log10(np.max(np.abs(y))),2),
                         rms_dbfs=round(20*np.log10(np.sqrt(np.mean(y*y))),2),
                         loop_seam_delta=seam,sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
        print(f"{field}: {len(y)/SR:.2f}s, {path.name}")
    manifest = dict(method="Original deterministic additive/subtractive synthesis; not AI audio generation.",
                    seed=20261025,sample_rate=SR,format="stereo PCM16 WAV",clips=rows)
    (ROOT/"AUDIO_MANIFEST.json").write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    lines=["# Cat Quest 声音提示词与素材", "", "制作方式：本地原创合成；没有调用 AI 音频模型、付费 API 或第三方采样。以下英文 prompt 是声音设计说明；实际由显式旋律、音色合成和包络参数实现，不是把自然语言交给模型。", "", "所有素材为 48 kHz / 16-bit / stereo WAV。BGM 为 16 小节循环，混响/延迟尾音回卷到开头；短音效首尾平滑。保留低于 0 dBFS 的余量以容纳同时播放。", ""]
    for r in rows:
        lines += [f"## {r['title']} — `{r['field']}`", "", f"文件：`{r['path']}`；时长 {r['seconds']} 秒"+(f"；{r['bpm']} BPM" if r['bpm'] else "")+"。", "", "> "+r['prompt'], ""]
    lines += ["## 调整与验证", "", "生成器：`Tools/Generate-CatQuestAudio.py`（Python + numpy）。默认拒绝覆盖；确认要替换整套音频后才使用 `--replace`。它不改 .meta、prefab 或玩家设置。", "", "机器检查：有效 PCM、非空信号、峰值无削波、循环接缝采样差；统计和 SHA-256 见 `AUDIO_MANIFEST.json`。这些不代替真实试听。首次试听请检查循环是否自然、战斗连续命中是否太密、提示音是否清楚。", ""]
    (ROOT/"SOUND_PROMPTS.md").write_text("\n".join(lines),encoding="utf-8")


if __name__ == "__main__":
    main()
