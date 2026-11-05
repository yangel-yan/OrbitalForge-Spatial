"""Read-only WAV/mix verification; writes only the Ceremony-Pass JSON report.

Requires numpy and scipy. This is waveform mathematics, not a native PICO
listening test or a claim about Unity's resampler, routing, or final device gain.
"""
from pathlib import Path
from math import gcd
import hashlib
import json
import wave
import numpy as np
import scipy
from scipy.signal import resample_poly


ROOT = Path(__file__).resolve().parents[1]
AUDIO = ROOT / "Assets" / "SpaceBall" / "Audio"
REPORT = ROOT / "Builds" / "SpaceBall" / "Ceremony-Pass" / "audio-verification.json"
RATE = 48000
PATHS = {
    "ChargeReady": AUDIO / "Interaction" / "ChargeReady.wav",
    "GatePower": AUDIO / "Interaction" / "GatePower.wav",
    "DockLock": AUDIO / "Interaction" / "DockLock.wav",
    "Launch": AUDIO / "Interaction" / "Launch.wav",
    "RelayA": AUDIO / "RelayA.wav",
    "RelayB": AUDIO / "RelayB.wav",
    "Delivery": AUDIO / "Delivery.wav",
}


def waveform_stats(samples):
    peak = float(np.max(np.abs(samples)))
    rms = float(np.sqrt(np.mean(samples * samples)))
    return {
        "peakLinear": peak, "peakDbfs": float(20 * np.log10(peak)),
        "headroomDb": float(-20 * np.log10(peak)),
        "rmsLinear": rms, "rmsDbfs": float(20 * np.log10(rms)),
        "dcLinear": float(np.mean(samples)),
        "clippedSamples": int(np.count_nonzero(np.abs(samples) > 1)),
    }


def read_clip(path):
    source_bytes = path.read_bytes()
    with wave.open(str(path), "rb") as source:
        channels, width, rate, count, compression, _ = source.getparams()
        if channels != 1 or width != 2 or compression != "NONE":
            raise ValueError("Expected mono PCM16 WAV: " + str(path))
        pcm = np.frombuffer(source.readframes(count), dtype="<i2")
    samples = pcm.astype(np.float64) / 32768
    metadata = {
        "file": path.relative_to(ROOT).as_posix(),
        "sha256": hashlib.sha256(source_bytes).hexdigest(), "bytes": len(source_bytes),
        "sampleRate": rate, "channels": channels, "bitsPerSample": width * 8,
        "sampleCount": count, "durationSeconds": count / rate,
        "firstSampleInt16": int(pcm[0]), "lastSampleInt16": int(pcm[-1]),
        "first8SamplesInt16": pcm[:8].tolist(), "last8SamplesInt16": pcm[-8:].tolist(),
        **waveform_stats(samples),
    }
    source_spectrum = np.abs(np.fft.rfft(samples)) ** 2
    frequency = np.fft.rfftfreq(samples.size, 1 / rate)
    metadata["energyBelow80HzFraction"] = float(source_spectrum[frequency < 80].sum() / source_spectrum.sum())
    metadata["energyAbove3000HzFraction"] = float(source_spectrum[frequency > 3000].sum() / source_spectrum.sum())
    if rate != RATE:
        factor = gcd(rate, RATE)
        samples = resample_poly(samples, RATE // factor, rate // factor)
    metadata["mixSampleRate"] = RATE
    metadata["resampledPeakLinear"] = float(np.max(np.abs(samples)))
    return samples, metadata


def mix(clips, events):
    signal = np.zeros(max(round(start * RATE) + len(clips[name]) for name, gain, start in events))
    for name, gain, start in events:
        offset = round(start * RATE)
        signal[offset:offset + len(clips[name])] += gain * clips[name]
    return {
        "events": [{"name": name, "gain": gain, "startSeconds": start} for name, gain, start in events],
        **waveform_stats(signal),
        "peakTimeSeconds": int(np.argmax(np.abs(signal))) / RATE,
        "nonClipping": bool(np.max(np.abs(signal)) <= 1),
    }


def verify_charge_offsets(clips):
    charge, launch = clips["ChargeReady"] * .65, clips["Launch"] * .65
    # At charge sample i, a nonnegative launch delay permits launch samples
    # j<=i. Since max delay is the full charge length, its lower bound is 0.
    # Prefix extrema give the exact peak over every allowed (i,j) pair, rather
    # than approximating delays with a coarse sweep or correlating RMS energy.
    charge_indices = np.arange(len(charge))
    last_launch_index = np.minimum(charge_indices, len(launch) - 1)
    maximum = charge + np.maximum.accumulate(launch)[last_launch_index]
    minimum = charge + np.minimum.accumulate(launch)[last_launch_index]
    highest, lowest = int(np.argmax(maximum)), int(np.argmin(minimum))
    if maximum[highest] >= -minimum[lowest]:
        i = highest
        j = int(np.argmax(launch[:last_launch_index[i] + 1]))
    else:
        i = lowest
        j = int(np.argmin(launch[:last_launch_index[i] + 1]))
    delay = i - j
    worst = mix(clips, [("ChargeReady", .65, 0), ("Launch", .65, delay / RATE)])
    peak = max(float(np.max(maximum)), float(-np.min(minimum)),
               float(np.max(np.abs(charge))), float(np.max(np.abs(launch))))
    assert abs(worst["peakLinear"] - peak) < 1e-12
    worst.update({
        "method": "Exact prefix-extrema maximization over all sample-aligned Launch delays 0..21120, independently checked by constructing the maximizing mix",
        "delaySamplesInclusive": [0, round(.44 * RATE)],
        "delayCount": round(.44 * RATE) + 1, "delayResolutionSeconds": 1 / RATE,
        "worstLaunchDelaySamples": delay, "worstLaunchDelaySeconds": delay / RATE,
        "worstPeakAcrossAllOffsets": peak,
        "allOffsetsNonClipping": peak <= 1,
        "sumOfIndividualPeaksUpperBound": float(np.max(np.abs(charge)) + np.max(np.abs(launch))),
    })
    return worst


def main():
    clips, metadata = {}, {}
    for name, path in PATHS.items():
        clips[name], metadata[name] = read_clip(path)
    new_names = ("ChargeReady", "GatePower", "DockLock")
    for index, name in enumerate(new_names, 8):
        info = metadata[name]
        info["enumIndex"] = index
        assert info["sampleRate"] == RATE and info["peakDbfs"] <= -3
        assert info["firstSampleInt16"] == 0 and info["lastSampleInt16"] == 0
        assert abs(info["dcLinear"]) < 1e-5
    mixes = {
        "chargeReadyPlusLaunchAllDelays": verify_charge_offsets(clips),
        "gatePowerPlusRelayA": mix(clips, [("GatePower", .65, 0), ("RelayA", 1, 0)]),
        "gatePowerPlusRelayB": mix(clips, [("GatePower", .65, 0), ("RelayB", 1, 0)]),
        "dockLockThenDelivery": mix(clips, [("DockLock", .65, .80), ("Delivery", 1, .86)]),
    }
    assert all(item["nonClipping"] for item in mixes.values())
    # Verify that analysis has left every source WAV byte-for-byte intact.
    assert all(hashlib.sha256(PATHS[name].read_bytes()).hexdigest() == info["sha256"]
               for name, info in metadata.items())
    report = {
        "status": "passed", "verificationKind": "Offline mathematical waveform/mix check",
        "nativeListeningTest": False,
        "limits": "Not a native PICO listening test. Does not validate device output, Unity resampling/routing, final master gain or unrelated concurrently playing cues.",
        "reproduce": "python Tools/verify_ceremony_audio.py",
        "numpyVersion": np.__version__, "scipyVersion": scipy.__version__,
        "resampling": "24 kHz sources converted to 48 kHz using scipy.signal.resample_poly with default Kaiser beta=5.0; float64 linear summing, no normalization or limiter",
        "sourceWavsUnchanged": True, "sourceClips": metadata, "mixes": mixes,
        "gainAdjustmentRequired": False,
    }
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"status": report["status"], "report": str(REPORT),
                      "peakDbfs": {name: value["peakDbfs"] for name, value in mixes.items()},
                      "gainAdjustmentRequired": False}, indent=2))


if __name__ == "__main__":
    main()
