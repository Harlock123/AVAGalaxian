#!/usr/bin/env python3
"""Synthesises AVAGalaxian's sound effects and tunes into ../src/AVAGalaxian/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAGalaxian", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The fighter's missile: a quick rising zip; the aliens' shots, a thin blip.
    write("Fire.wav", mix(tone(0.16, 600, 2600, "square", release=1.0), tone(0.05, 4000, 2500, "noise", release=1.0, gain=0.2)), 0.24)
    write("AlienFire.wav", tone(0.07, 1500, 900, "pulse", release=0.8), 0.16)
    # An alien hit: a crunchy pop; a commander, a deeper burst with a ring after it.
    write("Hit.wav", mix(tone(0.2, 3200, 200, "noise", release=0.9), tone(0.14, 1200, 300, "square", release=0.8, gain=0.5)), 0.38)
    write("CommanderHit.wav", cat(mix(tone(0.3, 3000, 150, "noise", release=0.7), tone(0.3, 700, 120, "saw", release=0.7, gain=0.7)),
                                  tone(0.3, 1800, 1700, "tri", release=0.7, vibrato=0.05, vib_rate=20, gain=0.5)), 0.45)
    # The big bonus: the burst and a run up the scale.
    write("Bonus.wav", cat(mix(tone(0.25, 3000, 150, "noise", release=0.7), tone(0.25, 700, 120, "saw", release=0.7, gain=0.7)),
                           *[tone(0.06, hz(n), kind="pulse", release=0.4) for n in ["C5", "E5", "G5", "C6", "E6", "G6", "C7"]]), 0.42)
    # Peeling off: a falling, wavering whistle; a commander's escort joining, a two-note call over it.
    write("Dive.wav", tone(0.9, 1500, 350, "tri", release=0.4, vibrato=0.05, vib_rate=11), 0.22)
    write("Escort.wav", cat(tone(0.1, hz("E6"), kind="pulse", release=0.5), tone(0.14, hz("B5"), kind="pulse", release=0.6)), 0.24)
    write("Death.wav", cat(mix(tone(0.9, 3500, 90, "noise", release=0.6), tone(0.9, 420, 40, "saw", release=0.6, gain=0.7)),
                           *[tone(0.14, hz(n), kind="tri", release=0.4) for n in ["D5", "B4", "G4", "D4"]]), 0.5)
    write("WaveStart.wav", tune(176, [("G4", 0.5), ("C5", 0.5), ("F5", 0.5), ("E5", 0.5), ("D5", 0.5), ("G5", 1.5)],
                                [("C3", 1), ("G2", 1), ("C3", 2)]), 0.42)
    write("Clear.wav", tune(190, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("B5", 0.5), ("G5", 0.5), ("C6", 2)], [("C3", 1), ("G2", 1), ("C3", 2)]), 0.42)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["G5", "B5", "D6", "G6", "D6", "G6"]]), 0.4)

    # The swarm's hum: a low, slowly wavering drone with a soft whirr over it. Loops (four seconds, whole cycles).
    n = int(RATE * 4)
    hum = []
    for i in range(n):
        t = i / RATE
        swell = 0.6 + 0.4 * math.sin(2 * math.pi * 0.5 * t) ** 2
        hum.append(swell * (math.sin(2 * math.pi * 82.5 * t + 0.6 * math.sin(2 * math.pi * 3 * t)) * 0.55
                            + math.sin(2 * math.pi * 165 * t) * 0.18)
                   + 0.05 * math.sin(2 * math.pi * (660 + 30 * math.sin(2 * math.pi * 1.0 * t)) * t))
    write("Music.wav", hum, 0.28)

    # The title: a bright space fanfare in G, rising in fourths, over a pulsing bass (an original tune).
    lead = [("D4", 1), ("G4", 1), ("C5", 2), ("B4", 1), ("A4", 1), ("G4", 2),
            ("E4", 1), ("A4", 1), ("D5", 2), ("C5", 1), ("B4", 1), ("A4", 2),
            ("B4", 1), ("C5", 1), ("D5", 1), ("E5", 1), ("F#5", 1), ("G5", 1), ("A5", 2),
            ("G5", 1), ("D5", 1), ("B4", 1), ("D5", 1), ("G5", 4)]
    bass = [("G2", 2), ("D3", 2), ("G2", 2), ("D3", 2),
            ("A2", 2), ("E3", 2), ("D3", 2), ("F#2", 2),
            ("G2", 2), ("B2", 2), ("D3", 2), ("D2", 2),
            ("G2", 2), ("D3", 2), ("G2", 4)]
    write("TitleTune.wav", cat(tune(200, lead, bass, kind="pulse"), silence(1.2)), 0.42)
    write("StartTune.wav", tune(168, [("D5", 0.5), ("G5", 0.5), ("B5", 0.5), ("A5", 0.5), ("G5", 0.5), ("D6", 2)], [("G2", 1), ("D3", 1), ("G2", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(80, [("D5", 1), ("B4", 1), ("A4", 1), ("F#4", 1), ("E4", 1), ("G4", 3)], [("G3", 2), ("D3", 2), ("C3", 2), ("G2", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
