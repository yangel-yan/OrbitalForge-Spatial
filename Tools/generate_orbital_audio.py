"""Original, deterministic UI/energy cues. No samples or third-party audio."""
import math
import struct
import wave
from pathlib import Path

RATE = 24000
OUT = Path(__file__).resolve().parents[1] / "Assets/SpaceBall/Audio"
OUT.mkdir(parents=True, exist_ok=True)

def cue(name, duration, notes):
    data = []
    for i in range(int(duration * RATE)):
        t = i / RATE
        value = 0.0
        for start, length, frequency, gain in notes:
            age = t - start
            if 0 <= age <= length:
                envelope = min(1, age / .008) * (1 - age / length) ** 2
                value += gain * envelope * (math.sin(2 * math.pi * frequency * age)
                    + .12 * math.sin(4 * math.pi * frequency * age))
        data.append(struct.pack('<h', round(max(-.85, min(.85, value)) * 32767)))
    with wave.open(str(OUT / (name + '.wav')), 'wb') as output:
        output.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
        output.writeframes(b''.join(data))

cue('Assembly', .10, [(0, .08, 740, .17), (.012, .07, 1110, .06)])
cue('RelayA', .32, [(0, .24, 523.25, .20), (.08, .22, 783.99, .09)])
cue('RelayB', .38, [(0, .24, 659.25, .18), (.10, .25, 1046.5, .09)])
cue('Delivery', .95, [(0, .50, 523.25, .13), (.12, .55, 659.25, .13),
    (.24, .65, 783.99, .13), (.38, .52, 1046.5, .09)])
print('Generated 4 original mono cues in', OUT)
