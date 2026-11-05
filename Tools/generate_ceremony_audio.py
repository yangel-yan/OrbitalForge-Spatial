"""Deterministic original mechanical/energy cues for Orbital Courier.

Run from any directory with Python + numpy. Writes only the three named WAVs
and prints analysis of the saved 48 kHz / 16-bit / mono PCM files. No external
audio, API, AI provider, or sampled recording is used.
"""
from pathlib import Path
import hashlib
import json
import wave
import numpy as np


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "Assets" / "SpaceBall" / "Audio" / "Interaction"
RATE = 48000
PEAK_DBFS = -5.0
PEAK = 10 ** (PEAK_DBFS / 20)


def band_filter(signal, low, high):
    frequencies = np.fft.rfftfreq(signal.size, 1 / RATE)
    # Smooth spectral shoulders avoid a hard brick-wall impulse response.
    rise = np.clip((frequencies - low) / 35, 0, 1)
    fall = np.clip((high - frequencies) / 300, 0, 1)
    response = np.sin(rise * np.pi / 2) ** 2 * np.sin(fall * np.pi / 2) ** 2
    return np.fft.irfft(np.fft.rfft(signal) * response, n=signal.size)


def noise(rng, count, low, high):
    signal = band_filter(rng.standard_normal(count), low, high)
    return signal / np.sqrt(np.mean(signal * signal))


def window(time, start, end, attack, release):
    attack_phase = np.clip((time - start) / attack, 0, 1)
    release_phase = np.clip((end - time) / release, 0, 1)
    return np.sin(attack_phase * np.pi / 2) ** 2 * np.sin(release_phase * np.pi / 2) ** 2


def latch(time, start, amplitude, rng, frequencies, decay):
    local = np.maximum(time - start, 0)
    onset = np.sin(np.clip(local / .0025, 0, 1) * np.pi / 2) ** 2
    modes = sum(np.sin(2 * np.pi * frequency * local + index * .17) *
                np.exp(-local / (decay / (1 + index * .45))) / (1 + index * .7)
                for index, frequency in enumerate(frequencies))
    contact = noise(rng, time.size, 300, 2100) * np.exp(-local / .008)
    return amplitude * onset * (modes + .18 * contact) * (time >= start)


def charge_ready():
    rng = np.random.default_rng(2026110508)
    time = np.arange(round(.44 * RATE)) / RATE
    # A rounded transformer/motor rise with inharmonic upper resonances and
    # breath-like filtered energy. The final quiet latch supplies confirmation.
    phase = 2 * np.pi * (175 * time + 260 * time * time)
    body = (np.sin(phase) + .20 * np.sin(phase * 2.03) + .09 * np.sin(phase * 3.11))
    body *= window(time, 0, .405, .055, .095) * (.32 + .50 * time / .44)
    air = noise(rng, time.size, 260, 1800) * window(time, .035, .39, .09, .11)
    air *= .105 * (1 + .12 * np.sin(2 * np.pi * 19 * time))
    click = latch(time, .315, .18, rng, [380, 665, 1037, 1565], .024)
    return body + air + click


def gate_power():
    rng = np.random.default_rng(2026110509)
    time = np.arange(round(.54 * RATE)) / RATE
    # Two unequal mechanical release contacts precede a short, hollow energy
    # passage; the body stays above 100 Hz rather than adding sub-bass impact.
    release = latch(time, .012, .43, rng, [125, 285, 563, 1041], .047)
    release += latch(time, .102, .31, rng, [170, 367, 711, 1387], .034)
    phase = 2 * np.pi * (190 * time + 135 * time * time)
    flux = (.23 * np.sin(phase) + .065 * np.sin(phase * 2.71))
    flux *= window(time, .095, .48, .09, .14)
    flow = noise(rng, time.size, 160, 1550) * window(time, .095, .485, .085, .145)
    flow *= .11 * (.85 + .15 * np.cos(2 * np.pi * 23 * time))
    return release + flux + flow


def dock_lock():
    rng = np.random.default_rng(2026110510)
    time = np.arange(round(.34 * RATE)) / RATE
    # Close, damped double catches with different modal spectra, rather than
    # a pitch-shifted notification or a second victory melody.
    first = latch(time, .012, .54, rng, [145, 338, 681, 1227], .034)
    second = latch(time, .158, .63, rng, [177, 412, 829, 1511], .040)
    friction = noise(rng, time.size, 230, 1100) * window(time, .022, .105, .008, .035) * .045
    return first + second + friction


def prepare(signal):
    signal = band_filter(signal, 80, 2800)
    time = np.arange(signal.size) / RATE
    fade = window(time, 0, time[-1], .006, .025)
    signal *= fade
    # Remove DC using the same smooth window, retaining exact zero endpoints.
    signal -= fade * signal.sum() / fade.sum()
    signal *= PEAK / np.max(np.abs(signal))
    return np.rint(signal * 32767).astype("<i2")


def write_and_analyze(name, signal, index):
    path = OUTPUT / (name + ".wav")
    pcm = prepare(signal)
    with wave.open(str(path), "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes(pcm.tobytes())
    with wave.open(str(path), "rb") as saved:
        assert saved.getnchannels() == 1 and saved.getsampwidth() == 2 and saved.getframerate() == RATE
        samples = np.frombuffer(saved.readframes(saved.getnframes()), dtype="<i2").astype(np.float64) / 32768
    peak, rms, dc = np.max(np.abs(samples)), np.sqrt(np.mean(samples * samples)), np.mean(samples)
    frequencies = np.fft.rfftfreq(samples.size, 1 / RATE)
    spectrum = np.abs(np.fft.rfft(samples)) ** 2
    assert peak <= 10 ** (-3 / 20) and np.isfinite(samples).all()
    assert samples[0] == 0 and samples[-1] == 0 and abs(dc) < 1e-5
    return {
        "name": name, "enumIndex": index, "file": str(path.relative_to(ROOT)),
        "durationSeconds": samples.size / RATE, "sampleCount": int(samples.size),
        "sampleRate": RATE, "channels": 1, "bitsPerSample": 16,
        "peakLinear": float(peak), "peakDbfs": float(20 * np.log10(peak)),
        "rmsLinear": float(rms), "rmsDbfs": float(20 * np.log10(rms)),
        "dcLinear": float(dc), "firstSampleInt16": int(pcm[0]), "lastSampleInt16": int(pcm[-1]),
        "first8SamplesInt16": pcm[:8].tolist(), "last8SamplesInt16": pcm[-8:].tolist(),
        "energyBelow80HzFraction": float(spectrum[frequencies < 80].sum() / spectrum.sum()),
        "energyAbove3000HzFraction": float(spectrum[frequencies > 3000].sum() / spectrum.sum()),
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
    }


def main():
    report = {"method": "Original deterministic procedural synthesis: damped inharmonic modal contacts, band-limited noise and motor/energy sweeps; no external recordings or service calls",
              "masterPeakTargetDbfs": PEAK_DBFS,
              "cues": [write_and_analyze("ChargeReady", charge_ready(), 8),
                       write_and_analyze("GatePower", gate_power(), 9),
                       write_and_analyze("DockLock", dock_lock(), 10)]}
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
