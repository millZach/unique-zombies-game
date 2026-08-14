#!/usr/bin/env python3
"""
Ashfall: Black Meridian -- procedural audio source generator.

Every sound in the game is synthesised here from arithmetic. Nothing is
downloaded, sampled, or derived from any existing recording, so the whole audio
set is original work with the same provenance story as the geometry and
textures: it is generated from source that lives in this repository.

Run it:

    /usr/bin/python3 Tools/Audio/generate_audio.py

Output lands in ``Assets/Ashfall/Audio`` as 16-bit PCM WAV. The generator is
deterministic -- each clip seeds its own RNG from a hash of its name -- so
re-running it produces byte-identical files and a clean ``git status``.

Design notes
------------
*Layers, not oscillators.* A gunshot that is one noise burst sounds like a
noise burst. The recipes below stack a transient, a body, a mechanical click
and a room tail, because that separation is what the ear actually uses to tell
a pistol from a shotgun.

*The station is the reverb.* Every combat sound gets the same short concrete
tail from :func:`room_tail`. One shared space is most of what makes a set of
sounds feel like they belong to one game.

*Voices are formants.* The enemy vocalisations are a driven glottal source
through three band-pass formants. That is a crude vocal-tract model, but it is
the difference between "a growl" and "filtered noise".
"""

from __future__ import annotations

import hashlib
import math
import struct
import sys
import wave
from pathlib import Path

import numpy as np

SR = 44100
REPO_ROOT = Path(__file__).resolve().parents[2]
OUT_DIR = REPO_ROOT / "Assets" / "Ashfall" / "Audio"


# ---------------------------------------------------------------------------
# Determinism
# ---------------------------------------------------------------------------


def rng_for(name: str) -> np.random.Generator:
    """A generator seeded from the clip name, so output never drifts."""
    digest = hashlib.sha256(name.encode("utf-8")).digest()
    seed = int.from_bytes(digest[:8], "little")
    return np.random.default_rng(seed)


def n_samples(seconds: float) -> int:
    return int(round(seconds * SR))


def t_axis(seconds: float) -> np.ndarray:
    return np.arange(n_samples(seconds), dtype=np.float64) / SR


# ---------------------------------------------------------------------------
# Noise sources
# ---------------------------------------------------------------------------


def white(n: int, rng: np.random.Generator) -> np.ndarray:
    return rng.standard_normal(n)


def _spectral_noise(n: int, rng: np.random.Generator, exponent: float) -> np.ndarray:
    """
    Coloured noise by shaping white noise in the frequency domain.

    A cascade of one-pole filters is the usual trick, but it is a recursion, and
    a pure-Python recursion over half a million samples is the difference
    between this script taking seconds and taking minutes. Shaping the spectrum
    directly is both exact and instant.
    """
    if n <= 1:
        return np.zeros(n)

    spectrum = np.fft.rfft(white(n, rng))
    freqs = np.fft.rfftfreq(n, 1.0 / SR)
    scale = np.ones_like(freqs)
    scale[1:] = freqs[1:] ** (-exponent)
    scale[0] = 0.0  # no DC: it only ever shows up as an offset
    out = np.fft.irfft(spectrum * scale, n)
    return out / (np.max(np.abs(out)) + 1e-12)


def pink(n: int, rng: np.random.Generator) -> np.ndarray:
    """-3 dB/octave. The natural spectrum for wind and distant weather."""
    return _spectral_noise(n, rng, 0.5)


def brown(n: int, rng: np.random.Generator) -> np.ndarray:
    """-6 dB/octave. Sea swell, thunder body, structural rumble."""
    return _spectral_noise(n, rng, 1.0)


def wander(n: int, rate_hz: float, rng: np.random.Generator) -> np.ndarray:
    """
    Smooth random modulation in -1..1, for gusts, vibrato and density drift.

    Modulators run far below the audio band, and a biquad tuned to a fraction
    of a hertz at 44.1 kHz is numerically miserable. Interpolating between
    random control points gives the same shape with none of that.
    """
    points = max(2, int(math.ceil(n / SR * max(rate_hz, 0.02))) + 2)
    control = rng.uniform(-1.0, 1.0, points)
    # Cosine interpolation: continuous first derivative, so no audible corners.
    x = np.linspace(0.0, points - 1, n)
    i = np.clip(x.astype(int), 0, points - 2)
    frac = x - i
    smooth = 0.5 - 0.5 * np.cos(frac * np.pi)
    return control[i] * (1.0 - smooth) + control[i + 1] * smooth


# ---------------------------------------------------------------------------
# Filters (RBJ biquad cookbook, direct form I)
# ---------------------------------------------------------------------------


_FFT_FILTER_THRESHOLD = 4096


def _biquad(x: np.ndarray, b: tuple[float, float, float], a: tuple[float, float, float]) -> np.ndarray:
    """
    Direct form I for short signals, frequency-domain for long ones.

    Both paths compute the same thing -- a biquad is linear and
    time-invariant, so multiplying by H(e^jw) over a zero-padded FFT is the
    filter, not an approximation of it. The padding is generous enough that the
    tail of the impulse response never wraps back onto the head.
    """
    n = len(x)
    if n == 0:
        return x

    if n < _FFT_FILTER_THRESHOLD:
        b0, b1, b2 = (c / a[0] for c in b)
        a1, a2 = a[1] / a[0], a[2] / a[0]
        y = np.empty_like(x)
        x1 = x2 = y1 = y2 = 0.0
        for i in range(n):
            xi = x[i]
            yi = b0 * xi + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
            y[i] = yi
            x2, x1 = x1, xi
            y2, y1 = y1, yi
        return y

    nfft = 1 << int(math.ceil(math.log2(2 * n)))
    spectrum = np.fft.rfft(x, nfft)
    z = np.exp(-2j * np.pi * np.arange(nfft // 2 + 1) / nfft)
    h = (b[0] + b[1] * z + b[2] * z * z) / (a[0] + a[1] * z + a[2] * z * z)
    return np.fft.irfft(spectrum * h, nfft)[:n]


def _rbj(kind: str, f0: float, q: float) -> tuple[tuple, tuple]:
    # Modulators legitimately sit below 1 Hz; the floor only guards against a
    # zero or negative frequency reaching the trig.
    f0 = min(max(f0, 0.05), SR * 0.45)
    w0 = 2.0 * math.pi * f0 / SR
    cos_w0, sin_w0 = math.cos(w0), math.sin(w0)
    alpha = sin_w0 / (2.0 * q)

    if kind == "lp":
        b = ((1 - cos_w0) / 2, 1 - cos_w0, (1 - cos_w0) / 2)
    elif kind == "hp":
        b = ((1 + cos_w0) / 2, -(1 + cos_w0), (1 + cos_w0) / 2)
    elif kind == "bp":
        b = (alpha, 0.0, -alpha)
    else:
        raise ValueError(kind)

    a = (1 + alpha, -2 * cos_w0, 1 - alpha)
    return b, a


def lowpass(x: np.ndarray, f0: float, q: float = 0.707) -> np.ndarray:
    b, a = _rbj("lp", f0, q)
    return _biquad(x, b, a)


def highpass(x: np.ndarray, f0: float, q: float = 0.707) -> np.ndarray:
    b, a = _rbj("hp", f0, q)
    return _biquad(x, b, a)


def bandpass(x: np.ndarray, f0: float, q: float = 2.0) -> np.ndarray:
    b, a = _rbj("bp", f0, q)
    return _biquad(x, b, a)


def resonator(x: np.ndarray, f0: float, q: float, gain: float = 1.0) -> np.ndarray:
    """A narrow band-pass used as a struck-mode filter for metal and bells."""
    return gain * bandpass(x, f0, q)


# ---------------------------------------------------------------------------
# Envelopes
# ---------------------------------------------------------------------------


def exp_decay(n: int, tau: float, attack: float = 0.0008) -> np.ndarray:
    t = np.arange(n) / SR
    rise = np.clip(t / max(attack, 1e-6), 0.0, 1.0)
    return rise * np.exp(-t / max(tau, 1e-6))


def adsr(n: int, a: float, d: float, s: float, r: float, sustain: float = 0.7) -> np.ndarray:
    """
    Attack/decay/sustain/release, truncated rather than overrun.

    Stages are clamped to whatever room is left, so an envelope longer than the
    buffer degrades into a shorter version of itself instead of throwing. That
    matters because several recipes reuse one envelope across clips of
    different lengths.
    """
    env = np.zeros(n)
    i = 0

    def stage(length: float, start: float, end: float) -> None:
        nonlocal i
        count = min(n_samples(length), n - i)
        if count <= 0:
            return
        env[i : i + count] = np.linspace(start, end, count)
        i += count

    stage(a, 0.0, 1.0)
    stage(d, 1.0, sustain)
    stage(s, sustain, sustain)
    stage(r, sustain, 0.0)
    return env


def fade(x: np.ndarray, in_ms: float = 2.0, out_ms: float = 12.0) -> np.ndarray:
    y = x.copy()
    ni, no = n_samples(in_ms / 1000.0), n_samples(out_ms / 1000.0)
    if ni and ni < len(y):
        y[:ni] *= np.linspace(0.0, 1.0, ni)
    if no and no < len(y):
        y[-no:] *= np.linspace(1.0, 0.0, no)
    return y


# ---------------------------------------------------------------------------
# Oscillators
# ---------------------------------------------------------------------------


def sine(freq: np.ndarray | float, n: int, phase: float = 0.0) -> np.ndarray:
    if np.isscalar(freq):
        freq = np.full(n, float(freq))
    return np.sin(2.0 * np.pi * np.cumsum(freq) / SR + phase)


def saw(freq: np.ndarray | float, n: int) -> np.ndarray:
    if np.isscalar(freq):
        freq = np.full(n, float(freq))
    ph = np.cumsum(freq) / SR
    return 2.0 * (ph - np.floor(ph + 0.5))


def sweep(f_start: float, f_end: float, n: int, curve: float = 2.0) -> np.ndarray:
    """Exponential-ish frequency contour. ``curve`` > 1 falls fast then settles."""
    t = np.linspace(0.0, 1.0, n)
    return f_start + (f_end - f_start) * (t**curve)


# ---------------------------------------------------------------------------
# Space
# ---------------------------------------------------------------------------


def room_tail(x: np.ndarray, seconds: float, mix: float, rng: np.random.Generator,
              damping: float = 3200.0) -> np.ndarray:
    """
    Convolve with a decaying noise burst: the station's concrete tail.

    A real Schroeder network would be cheaper, but at these lengths an FFT
    convolution is instant and the result has none of the metallic comb
    colouration that gives cheap reverbs away.
    """
    ir_n = n_samples(seconds)
    ir = white(ir_n, rng) * np.exp(-np.arange(ir_n) / (SR * seconds * 0.30))
    ir = lowpass(ir, damping, 0.6)
    ir[: n_samples(0.006)] *= np.linspace(0.0, 1.0, n_samples(0.006))
    ir /= np.sqrt(np.sum(ir**2)) + 1e-12

    wet = np.convolve(x, ir)
    out = np.zeros(len(wet))
    out[: len(x)] += x * (1.0 - mix)
    out += wet * mix
    return out


# ---------------------------------------------------------------------------
# Shaping and output
# ---------------------------------------------------------------------------


def soft_clip(x: np.ndarray, drive: float = 1.0) -> np.ndarray:
    return np.tanh(x * drive) / math.tanh(max(drive, 1e-6))


def dc_block(x: np.ndarray) -> np.ndarray:
    """
    Subsonic high-pass at 22 Hz.

    Brown noise and long pitch sweeps leave energy below what any speaker will
    reproduce. It is inaudible, it eats headroom that the audible part of the
    sound wants, and it shows up as a DC offset that clicks when a clip is cut
    off mid-play. Every clip gets this before it is normalised.
    """
    return highpass(x, 22.0, 0.6)


def normalize(x: np.ndarray, peak: float = 0.90) -> np.ndarray:
    y = dc_block(x)
    m = np.max(np.abs(y))
    return y if m < 1e-9 else y * (peak / m)


def write_wav(name: str, data: np.ndarray, stereo: bool = False) -> Path:
    """Writes 16-bit PCM. ``data`` is (n,) mono or (2, n) stereo."""
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    path = OUT_DIR / f"{name}.wav"

    if stereo:
        frames = np.stack(data, axis=-1)
    else:
        frames = data.reshape(-1, 1)

    clipped = np.clip(frames, -1.0, 1.0)
    ints = np.round(clipped * 32767.0).astype("<i2")

    with wave.open(str(path), "wb") as w:
        w.setnchannels(2 if stereo else 1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(ints.tobytes())

    return path


# ===========================================================================
# Recipes -- weapons
# ===========================================================================


def gunshot(name: str, *, body_hz: float, body_tau: float, thump_from: float,
            thump_to: float, thump_tau: float, crack_hz: float, tail_s: float,
            tail_mix: float, drive: float, action_delay: float,
            action_gain: float, length: float) -> np.ndarray:
    """
    The shared four-layer gunshot: crack, body, thump, mechanical action.

    Every parameter here changes something the ear separates out, which is why
    the three weapons are recognisably different instruments rather than the
    same sound at three pitches.
    """
    rng = rng_for(name)
    n = n_samples(length)

    # 1. Crack -- the supersonic snap. Very short, very bright.
    crack = bandpass(white(n, rng), crack_hz, 0.8) * exp_decay(n, 0.0022, 0.00005)
    crack += white(n, rng) * exp_decay(n, 0.0009, 0.00002) * 0.8

    # 2. Body -- the pressure wave leaving the barrel.
    body = bandpass(white(n, rng), body_hz, 0.7) * exp_decay(n, body_tau, 0.0004)
    body += bandpass(white(n, rng), body_hz * 2.1, 1.1) * exp_decay(n, body_tau * 0.55, 0.0003) * 0.6

    # 3. Thump -- a short pitch-dropping sine is what makes it feel like mass.
    thump = sine(sweep(thump_from, thump_to, n, 1.6), n) * exp_decay(n, thump_tau, 0.0012)

    # 4. Action -- bolt/slide cycling a beat later, so the gun reads mechanical.
    action = np.zeros(n)
    off = n_samples(action_delay)
    if off < n:
        m = n - off
        click = bandpass(white(m, rng), 2600.0, 1.4) * exp_decay(m, 0.010, 0.0002)
        click += resonator(white(m, rng), 5200.0, 9.0) * exp_decay(m, 0.020, 0.0004) * 0.4
        action[off:] = click * action_gain

    mix = soft_clip(crack * 0.55 + body * 1.0 + thump * 0.95 + action, drive)
    out = room_tail(mix, tail_s, tail_mix, rng)
    return normalize(fade(out, 0.4, 25.0), 0.94)


def sidearm_fire() -> np.ndarray:
    return gunshot(
        "SFX_Weapon_Sidearm_Fire",
        body_hz=760.0, body_tau=0.036, thump_from=170.0, thump_to=62.0,
        thump_tau=0.045, crack_hz=5200.0, tail_s=0.34, tail_mix=0.22,
        drive=1.7, action_delay=0.035, action_gain=0.34, length=0.42,
    )


def shotgun_fire() -> np.ndarray:
    name = "SFX_Weapon_Shotgun_Fire"
    base = gunshot(
        name,
        body_hz=340.0, body_tau=0.105, thump_from=125.0, thump_to=38.0,
        thump_tau=0.130, crack_hz=3400.0, tail_s=0.72, tail_mix=0.34,
        drive=2.3, action_delay=0.20, action_gain=0.55, length=0.95,
    )
    # A pumped shotgun racks after the shot. Two clunks, not one.
    rng = rng_for(name + "_pump")
    n = len(base)
    for delay, gain, hz in ((0.30, 0.32, 1500.0), (0.42, 0.38, 1150.0)):
        off = n_samples(delay)
        if off >= n:
            continue
        m = n - off
        clunk = bandpass(white(m, rng), hz, 1.2) * exp_decay(m, 0.026, 0.0004)
        clunk += sine(sweep(210.0, 140.0, m), m) * exp_decay(m, 0.020, 0.0008) * 0.5
        base[off:] += clunk * gain
    return normalize(base, 0.95)


def rifle_fire() -> np.ndarray:
    name = "SFX_Weapon_Rifle_Fire"
    base = gunshot(
        name,
        body_hz=980.0, body_tau=0.028, thump_from=200.0, thump_to=78.0,
        thump_tau=0.032, crack_hz=6400.0, tail_s=0.30, tail_mix=0.20,
        drive=1.9, action_delay=0.026, action_gain=0.30, length=0.36,
    )
    # The Arc-9 is a rail carbine: a coil discharge sits under the crack.
    rng = rng_for(name + "_coil")
    n = len(base)
    zap = sine(sweep(2600.0, 520.0, n, 3.0), n) * exp_decay(n, 0.030, 0.0003)
    zap *= 0.5 + 0.5 * sine(sweep(320.0, 90.0, n), n)  # ring modulation
    zap += bandpass(white(n, rng), 7200.0, 3.0) * exp_decay(n, 0.014, 0.0002) * 0.4
    return normalize(base + zap * 0.42, 0.94)


def reload_magazine() -> np.ndarray:
    """Mag release, mag out, fresh mag in, slide forward."""
    name = "SFX_Weapon_Reload_Mag"
    rng = rng_for(name)
    length = 1.15
    n = n_samples(length)
    out = np.zeros(n)

    def hit(at: float, hz: float, tau: float, gain: float, thump: float = 0.0):
        off = n_samples(at)
        if off >= n:
            return
        m = n - off
        seg = bandpass(white(m, rng), hz, 1.6) * exp_decay(m, tau, 0.0002)
        seg += resonator(white(m, rng), hz * 2.4, 8.0) * exp_decay(m, tau * 0.7, 0.0003) * 0.35
        if thump > 0.0:
            seg += sine(sweep(thump, thump * 0.55, m), m) * exp_decay(m, 0.030, 0.001) * 0.6
        out[off:] += seg * gain

    hit(0.00, 3100.0, 0.012, 0.55)                 # magazine catch
    hit(0.09, 1250.0, 0.030, 0.62, thump=150.0)    # magazine drops free
    hit(0.44, 1650.0, 0.036, 0.85, thump=125.0)    # fresh magazine seats
    hit(0.52, 2400.0, 0.014, 0.40)                 # latch
    hit(0.74, 2050.0, 0.028, 0.78, thump=185.0)    # slide forward
    hit(0.79, 4200.0, 0.010, 0.45)                 # slide stop

    # Cloth and hand movement under the metal, so it is not four bare clicks.
    cloth = bandpass(white(n, rng), 900.0, 0.6) * 0.05
    cloth *= np.clip(np.abs(sine(2.6, n)) ** 3, 0.0, 1.0)
    out += cloth

    out = room_tail(soft_clip(out, 1.2), 0.26, 0.16, rng)
    return normalize(fade(out, 1.0, 30.0), 0.82)


def reload_shell() -> np.ndarray:
    """One shell pushed into a tube: brass, then the lifter snapping back."""
    name = "SFX_Weapon_Reload_Shell"
    rng = rng_for(name)
    n = n_samples(0.42)

    brass = bandpass(white(n, rng), 2300.0, 1.1) * exp_decay(n, 0.022, 0.0003)
    brass += resonator(white(n, rng), 4800.0, 12.0) * exp_decay(n, 0.030, 0.0004) * 0.4

    seat = np.zeros(n)
    off = n_samples(0.085)
    m = n - off
    seat[off:] = bandpass(white(m, rng), 1200.0, 1.5) * exp_decay(m, 0.028, 0.0004)
    seat[off:] += sine(sweep(170.0, 110.0, m), m) * exp_decay(m, 0.022, 0.0009) * 0.5

    out = room_tail(soft_clip(brass * 0.7 + seat * 0.9, 1.2), 0.20, 0.15, rng)
    return normalize(fade(out, 0.5, 25.0), 0.80)


def dry_fire() -> np.ndarray:
    """A hollow firing-pin click. Deliberately thin: it has to read as failure."""
    name = "SFX_Weapon_DryFire"
    rng = rng_for(name)
    n = n_samples(0.16)
    click = bandpass(white(n, rng), 2900.0, 1.0) * exp_decay(n, 0.0055, 0.00008)
    click += resonator(white(n, rng), 6100.0, 14.0) * exp_decay(n, 0.012, 0.0002) * 0.5
    click += sine(sweep(420.0, 260.0, n), n) * exp_decay(n, 0.006, 0.0004) * 0.25
    out = room_tail(click, 0.18, 0.18, rng)
    return normalize(fade(out, 0.3, 20.0), 0.62)


def weapon_equip() -> np.ndarray:
    """Sling, hand on the grip, and the action checked."""
    name = "SFX_Weapon_Equip"
    rng = rng_for(name)
    n = n_samples(0.46)

    cloth = bandpass(white(n, rng), 1400.0, 0.5) * adsr(n, 0.02, 0.10, 0.02, 0.20, 0.35)
    cloth *= 0.35

    metal = np.zeros(n)
    for at, hz, gain in ((0.10, 1800.0, 0.5), (0.21, 2600.0, 0.35)):
        off = n_samples(at)
        m = n - off
        metal[off:] += bandpass(white(m, rng), hz, 1.6) * exp_decay(m, 0.020, 0.0003) * gain

    out = room_tail(soft_clip(cloth + metal, 1.1), 0.22, 0.16, rng)
    return normalize(fade(out, 1.0, 30.0), 0.66)


# ===========================================================================
# Recipes -- impacts
# ===========================================================================


def impact_flesh() -> np.ndarray:
    name = "SFX_Impact_Flesh"
    rng = rng_for(name)
    n = n_samples(0.26)
    wet = lowpass(white(n, rng), 900.0, 0.8) * exp_decay(n, 0.030, 0.0004)
    wet += bandpass(white(n, rng), 260.0, 0.9) * exp_decay(n, 0.055, 0.001) * 0.8
    thud = sine(sweep(150.0, 55.0, n, 1.8), n) * exp_decay(n, 0.040, 0.0012)
    out = room_tail(soft_clip(wet * 0.9 + thud * 0.7, 1.3), 0.18, 0.12, rng)
    return normalize(fade(out, 0.4, 20.0), 0.70)


def impact_critical() -> np.ndarray:
    """A head shot: the wet hit plus a storm-charge discharge on top."""
    name = "SFX_Impact_Critical"
    rng = rng_for(name)
    n = n_samples(0.40)
    crack = bandpass(white(n, rng), 2400.0, 1.0) * exp_decay(n, 0.010, 0.0001)
    wet = lowpass(white(n, rng), 1100.0, 0.8) * exp_decay(n, 0.024, 0.0003) * 0.8
    charge = sine(sweep(1500.0, 3200.0, n, 0.6), n) * exp_decay(n, 0.070, 0.0020) * 0.35
    charge *= 0.6 + 0.4 * sine(38.0, n)
    thud = sine(sweep(180.0, 60.0, n, 1.8), n) * exp_decay(n, 0.045, 0.0012) * 0.7
    out = room_tail(soft_clip(crack * 0.8 + wet + charge + thud, 1.4), 0.30, 0.20, rng)
    return normalize(fade(out, 0.3, 22.0), 0.80)


def impact_world() -> np.ndarray:
    """Concrete: a dry crack, grit falling, no low end to speak of."""
    name = "SFX_Impact_World"
    rng = rng_for(name)
    n = n_samples(0.32)
    crack = bandpass(white(n, rng), 3200.0, 0.9) * exp_decay(n, 0.008, 0.00008)
    dust = highpass(white(n, rng), 1800.0) * exp_decay(n, 0.055, 0.0008) * 0.30
    body = bandpass(white(n, rng), 620.0, 1.1) * exp_decay(n, 0.020, 0.0004) * 0.55
    grit = np.zeros(n)
    for at in rng.uniform(0.02, 0.16, 5):
        off = n_samples(float(at))
        m = n - off
        grit[off:] += bandpass(white(m, rng), float(rng.uniform(3500, 7500)), 6.0) \
            * exp_decay(m, 0.006, 0.0001) * 0.20
    out = room_tail(soft_clip(crack + dust + body + grit, 1.2), 0.24, 0.18, rng)
    return normalize(fade(out, 0.3, 20.0), 0.64)


def barricade_repair() -> np.ndarray:
    """Plank set, then two hammer strikes. Reads as work being done."""
    name = "SFX_Barricade_Repair"
    rng = rng_for(name)
    n = n_samples(0.85)
    out = np.zeros(n)

    def wood(at: float, hz: float, gain: float):
        off = n_samples(at)
        if off >= n:
            return
        m = n - off
        seg = bandpass(white(m, rng), hz, 1.3) * exp_decay(m, 0.030, 0.0004)
        seg += resonator(white(m, rng), hz * 1.9, 7.0) * exp_decay(m, 0.045, 0.0006) * 0.4
        seg += sine(sweep(hz * 0.30, hz * 0.18, m), m) * exp_decay(m, 0.035, 0.001) * 0.5
        out[off:] += seg * gain

    wood(0.00, 520.0, 0.75)   # plank dropped into place
    wood(0.26, 900.0, 0.85)   # hammer
    wood(0.46, 880.0, 0.70)   # hammer

    res = room_tail(soft_clip(out, 1.3), 0.30, 0.22, rng)
    return normalize(fade(res, 0.5, 28.0), 0.78)


# ===========================================================================
# Recipes -- enemy voices
# ===========================================================================


def voiced(n: int, f0: np.ndarray, formants: list[tuple[float, float, float]],
           rng: np.random.Generator, breath: float = 0.25,
           jitter: float = 0.03) -> np.ndarray:
    """
    A glottal source through band-pass formants.

    Crude as vocal-tract models go, but three resonances over a buzzing source
    is the whole difference between "a creature" and "filtered noise".
    """
    wobble = 1.0 + jitter * wander(n, 11.0, rng) * 1.6
    source = saw(f0 * wobble, n) * 0.75
    source += white(n, rng) * breath
    source = lowpass(source, 5200.0, 0.7)

    out = np.zeros(n)
    for hz, q, gain in formants:
        out += resonator(source, hz, q, gain)
    return out


def enemy_attack(name: str, *, f0_from: float, f0_to: float, length: float,
                 formants: list[tuple[float, float, float]], breath: float,
                 swipe_hz: float, swipe_gain: float, attack: float,
                 tail_mix: float) -> np.ndarray:
    rng = rng_for(name)
    n = n_samples(length)

    voice = voiced(n, sweep(f0_from, f0_to, n, 1.4), formants, rng, breath)
    voice *= adsr(n, attack, length * 0.25, length * 0.20, length * 0.45, 0.55)

    swipe = bandpass(white(n, rng), swipe_hz, 0.8)
    swipe *= exp_decay(n, length * 0.16, 0.004)
    swipe *= swipe_gain

    out = room_tail(soft_clip(voice * 0.9 + swipe, 1.5), 0.36, tail_mix, rng)
    return normalize(fade(out, 3.0, 40.0), 0.86)


def enemy_death(name: str, *, f0_from: float, f0_to: float, length: float,
                formants: list[tuple[float, float, float]], breath: float,
                collapse_hz: float, dissipate: float) -> np.ndarray:
    """Vocal falls away, body collapses, storm charge vents off."""
    rng = rng_for(name)
    n = n_samples(length)

    voice = voiced(n, sweep(f0_from, f0_to, n, 1.9), formants, rng, breath)
    voice *= adsr(n, 0.012, length * 0.30, length * 0.10, length * 0.58, 0.42)

    collapse = np.zeros(n)
    off = n_samples(length * 0.42)
    m = n - off
    collapse[off:] = bandpass(white(m, rng), collapse_hz, 0.9) * exp_decay(m, 0.10, 0.004)
    collapse[off:] += sine(sweep(110.0, 40.0, m, 1.6), m) * exp_decay(m, 0.16, 0.006) * 0.7

    vent = highpass(white(n, rng), 2600.0)
    vent *= exp_decay(n, length * 0.34, length * 0.30) * dissipate
    vent *= 0.6 + 0.4 * sine(sweep(90.0, 18.0, n), n)

    out = room_tail(soft_clip(voice * 0.85 + collapse * 0.8 + vent, 1.4), 0.55, 0.30, rng)
    return normalize(fade(out, 4.0, 90.0), 0.88)


def brute_attack() -> np.ndarray:
    """No voice: a hydraulic servo winding up and a plated fist landing."""
    name = "SFX_Enemy_Attack_Brute"
    rng = rng_for(name)
    n = n_samples(0.85)

    servo = saw(sweep(260.0, 150.0, n, 1.2), n) * 0.35
    servo = bandpass(servo, 900.0, 1.2)
    servo *= adsr(n, 0.02, 0.16, 0.06, 0.30, 0.45)

    slam = np.zeros(n)
    off = n_samples(0.34)
    m = n - off
    slam[off:] = bandpass(white(m, rng), 480.0, 0.8) * exp_decay(m, 0.055, 0.0004)
    for mode, q, g in ((320.0, 26.0, 0.45), (740.0, 32.0, 0.30), (1580.0, 40.0, 0.18)):
        slam[off:] += resonator(white(m, rng), mode, q, g) * exp_decay(m, 0.30, 0.001)
    slam[off:] += sine(sweep(96.0, 34.0, m, 1.7), m) * exp_decay(m, 0.11, 0.0016) * 1.1

    out = room_tail(soft_clip(servo * 0.7 + slam, 1.8), 0.60, 0.32, rng)
    return normalize(fade(out, 3.0, 60.0), 0.92)


def brute_death() -> np.ndarray:
    """The chest reactor lets go, then two tonnes of armour hits the deck."""
    name = "SFX_Enemy_Death_Brute"
    rng = rng_for(name)
    n = n_samples(1.9)

    whine = sine(sweep(420.0, 1900.0, n_samples(0.55), 0.7), n_samples(0.55))
    spool = np.zeros(n)
    spool[: len(whine)] = whine * np.linspace(0.0, 0.55, len(whine))

    burst = np.zeros(n)
    off = n_samples(0.55)
    m = n - off
    burst[off:] = white(m, rng) * exp_decay(m, 0.09, 0.0006)
    burst[off:] = bandpass(burst[off:], 1500.0, 0.5)
    burst[off:] += sine(sweep(220.0, 42.0, m, 1.5), m) * exp_decay(m, 0.20, 0.002) * 1.2

    fall = np.zeros(n)
    off2 = n_samples(0.95)
    m2 = n - off2
    for mode, q, g in ((240.0, 22.0, 0.40), (610.0, 28.0, 0.26), (1320.0, 34.0, 0.16)):
        fall[off2:] += resonator(white(m2, rng), mode, q, g) * exp_decay(m2, 0.36, 0.002)
    fall[off2:] += sine(sweep(80.0, 30.0, m2, 1.8), m2) * exp_decay(m2, 0.16, 0.003) * 0.9

    out = room_tail(soft_clip(spool * 0.5 + burst * 0.9 + fall, 1.6), 0.85, 0.34, rng)
    return normalize(fade(out, 4.0, 120.0), 0.93)


# ===========================================================================
# Recipes -- player
# ===========================================================================


def player_hurt() -> np.ndarray:
    """Heard from inside the helmet: muffled impact, sharp inward breath."""
    name = "SFX_Player_Hurt"
    rng = rng_for(name)
    n = n_samples(0.55)

    impact = lowpass(white(n, rng), 420.0, 0.8) * exp_decay(n, 0.045, 0.0006)
    impact += sine(sweep(120.0, 48.0, n, 1.6), n) * exp_decay(n, 0.060, 0.0015) * 0.8

    breath = np.zeros(n)
    off = n_samples(0.10)
    m = n - off
    b = bandpass(white(m, rng), 1150.0, 0.9) * adsr(m, 0.05, 0.09, 0.02, 0.16, 0.6)
    breath[off:] = b * 0.30

    ring = sine(sweep(3100.0, 2600.0, n), n) * exp_decay(n, 0.22, 0.010) * 0.10

    out = soft_clip(impact * 0.95 + breath + ring, 1.3)
    return normalize(fade(out, 1.0, 60.0), 0.78)


def player_down() -> np.ndarray:
    """Signal lost: heartbeat slowing under a descending drone."""
    name = "SFX_Player_Down"
    rng = rng_for(name)
    n = n_samples(2.6)

    drone = sine(sweep(180.0, 42.0, n, 1.3), n) * 0.55
    drone += sine(sweep(268.0, 63.0, n, 1.3), n) * 0.22
    drone *= adsr(n, 0.03, 0.60, 0.60, 1.30, 0.55)

    beats = np.zeros(n)
    for at, gain in ((0.06, 1.0), (0.34, 0.7), (0.92, 0.8), (1.18, 0.5), (1.90, 0.5)):
        off = n_samples(at)
        if off >= n:
            continue
        m = n - off
        beats[off:] += sine(sweep(72.0, 40.0, m, 2.0), m) * exp_decay(m, 0.075, 0.004) * gain

    hiss = highpass(white(n, rng), 4200.0) * np.linspace(0.10, 0.0, n)

    out = room_tail(soft_clip(drone + beats * 0.6 + hiss, 1.2), 0.90, 0.28, rng)
    return normalize(fade(out, 6.0, 180.0), 0.82)


def player_last_stand() -> np.ndarray:
    """The save. Rising, resolving, unmistakably good news."""
    name = "SFX_Player_LastStand"
    rng = rng_for(name)
    n = n_samples(1.25)

    out = np.zeros(n)
    for i, f in enumerate((329.63, 493.88, 659.25, 987.77)):
        off = n_samples(0.05 * i)
        m = n - off
        partial = sine(f, m) * exp_decay(m, 0.42, 0.006)
        partial += sine(f * 2.005, m) * exp_decay(m, 0.22, 0.006) * 0.35
        out[off:] += partial * (0.85 - 0.12 * i)

    shimmer = highpass(white(n, rng), 6500.0) * exp_decay(n, 0.30, 0.020) * 0.10
    swell = sine(sweep(90.0, 165.0, n, 0.8), n) * adsr(n, 0.18, 0.30, 0.10, 0.55, 0.5) * 0.30

    res = room_tail(soft_clip(out * 0.6 + shimmer + swell, 1.1), 0.70, 0.30, rng)
    return normalize(fade(res, 4.0, 140.0), 0.80)


# ===========================================================================
# Recipes -- world and run flow
# ===========================================================================


def powerup_pickup() -> np.ndarray:
    """Bright ascending arpeggio with an FM bell timbre."""
    name = "SFX_PowerUp_Pickup"
    rng = rng_for(name)
    n = n_samples(1.05)
    out = np.zeros(n)

    for i, f in enumerate((523.25, 659.25, 783.99, 1046.50, 1318.51)):
        off = n_samples(0.055 * i)
        m = n - off
        mod = sine(f * 2.01, m) * exp_decay(m, 0.08, 0.001) * 3.2
        bell = np.sin(2 * np.pi * f * np.arange(m) / SR + mod)
        out[off:] += bell * exp_decay(m, 0.26 - 0.03 * i, 0.0018) * (0.9 - 0.10 * i)

    shimmer = highpass(white(n, rng), 7000.0) * exp_decay(n, 0.22, 0.010) * 0.09
    res = room_tail(soft_clip(out * 0.5 + shimmer, 1.1), 0.55, 0.26, rng)
    return normalize(fade(res, 2.0, 120.0), 0.84)


def powerup_drop() -> np.ndarray:
    """A canister landing and arming itself: metal, then a slow pulse."""
    name = "SFX_PowerUp_Drop"
    rng = rng_for(name)
    n = n_samples(1.10)

    land = bandpass(white(n, rng), 1500.0, 1.2) * exp_decay(n, 0.030, 0.0003)
    for mode, q, g in ((420.0, 24.0, 0.35), (980.0, 30.0, 0.22)):
        land += resonator(white(n, rng), mode, q, g) * exp_decay(n, 0.22, 0.001)

    pulse = np.zeros(n)
    for at in (0.30, 0.62, 0.94):
        off = n_samples(at)
        if off >= n:
            continue
        m = n - off
        pulse[off:] += sine(sweep(1180.0, 1560.0, m, 0.5), m) * exp_decay(m, 0.055, 0.004) * 0.35

    res = room_tail(soft_clip(land * 0.8 + pulse, 1.2), 0.55, 0.28, rng)
    return normalize(fade(res, 1.0, 120.0), 0.72)


def purchase_route() -> np.ndarray:
    """Heavy shutter: latch releases, motor drives, structure settles."""
    name = "SFX_Purchase_Route"
    rng = rng_for(name)
    n = n_samples(1.9)

    latch = bandpass(white(n, rng), 2200.0, 1.4) * exp_decay(n, 0.018, 0.0002) * 0.55

    motor = np.zeros(n)
    off = n_samples(0.14)
    m = n - off
    drive = saw(sweep(58.0, 74.0, m, 0.6), m) * 0.5 + saw(sweep(116.0, 148.0, m, 0.6), m) * 0.22
    motor[off:] = bandpass(drive, 420.0, 0.8) * adsr(m, 0.10, 0.20, 0.70, 0.45, 0.75)
    motor[off:] += bandpass(white(m, rng), 1600.0, 0.5) * adsr(m, 0.10, 0.20, 0.70, 0.45, 0.20) * 0.18

    settle = np.zeros(n)
    off2 = n_samples(1.36)
    m2 = n - off2
    for mode, q, g in ((190.0, 20.0, 0.40), (520.0, 26.0, 0.24)):
        settle[off2:] += resonator(white(m2, rng), mode, q, g) * exp_decay(m2, 0.25, 0.0015)
    settle[off2:] += sine(sweep(88.0, 38.0, m2, 1.7), m2) * exp_decay(m2, 0.12, 0.002) * 0.7

    res = room_tail(soft_clip(latch + motor * 0.9 + settle, 1.4), 0.80, 0.30, rng)
    return normalize(fade(res, 3.0, 150.0), 0.86)


def purchase_weapon() -> np.ndarray:
    """Rack releases the weapon, then a short confirm interval."""
    name = "SFX_Purchase_Weapon"
    rng = rng_for(name)
    n = n_samples(1.05)

    clunk = bandpass(white(n, rng), 1250.0, 1.1) * exp_decay(n, 0.035, 0.0004)
    for mode, q, g in ((300.0, 22.0, 0.35), (860.0, 28.0, 0.22)):
        clunk += resonator(white(n, rng), mode, q, g) * exp_decay(n, 0.18, 0.001)
    clunk += sine(sweep(140.0, 60.0, n, 1.6), n) * exp_decay(n, 0.06, 0.0015) * 0.6

    confirm = np.zeros(n)
    for i, f in enumerate((587.33, 880.00)):
        off = n_samples(0.22 + 0.09 * i)
        m = n - off
        confirm[off:] += sine(f, m) * exp_decay(m, 0.20, 0.004) * (0.45 - 0.12 * i)
        confirm[off:] += sine(f * 2.0, m) * exp_decay(m, 0.10, 0.004) * 0.14

    res = room_tail(soft_clip(clunk * 0.85 + confirm, 1.2), 0.50, 0.26, rng)
    return normalize(fade(res, 1.0, 120.0), 0.82)


def purchase_denied() -> np.ndarray:
    """Dull, short, and low. Nothing about it should sound like progress."""
    name = "SFX_Purchase_Denied"
    rng = rng_for(name)
    n = n_samples(0.40)

    buzz = saw(112.0, n) * 0.5 + saw(113.7, n) * 0.5   # beating, sour
    buzz = lowpass(buzz, 700.0, 0.9) * adsr(n, 0.006, 0.06, 0.09, 0.20, 0.6)
    thunk = bandpass(white(n, rng), 380.0, 1.0) * exp_decay(n, 0.020, 0.0004) * 0.5

    res = room_tail(soft_clip(buzz * 0.7 + thunk, 1.3), 0.22, 0.16, rng)
    return normalize(fade(res, 1.0, 60.0), 0.60)


def round_start() -> np.ndarray:
    """Station klaxon: two-tone, three cycles, over a rising sub swell."""
    name = "SFX_Round_Start"
    rng = rng_for(name)
    n = n_samples(2.6)
    out = np.zeros(n)

    for i in range(3):
        for j, f in enumerate((392.0, 311.13)):
            off = n_samples(0.32 * (2 * i + j))
            if off >= n:
                continue
            m = min(n - off, n_samples(0.30))
            tone = saw(f, m) * 0.45 + sine(f, m) * 0.55
            tone = lowpass(tone, f * 4.5, 1.1)
            tone *= adsr(m, 0.020, 0.05, 0.14, 0.09, 0.85)
            out[off : off + m] += tone * (0.62 - 0.10 * i)

    swell = sine(sweep(46.0, 92.0, n, 0.7), n) * adsr(n, 0.35, 0.40, 0.60, 1.10, 0.6) * 0.42
    air = bandpass(white(n, rng), 2400.0, 0.5) * adsr(n, 0.30, 0.50, 0.40, 1.20, 0.18) * 0.10

    res = room_tail(soft_clip(out * 0.75 + swell + air, 1.3), 1.10, 0.34, rng)
    return normalize(fade(res, 4.0, 200.0), 0.88)


def round_complete() -> np.ndarray:
    """A resolving major triad with a low confirm underneath. Earned, not cute."""
    name = "SFX_Round_Complete"
    rng = rng_for(name)
    n = n_samples(2.2)
    out = np.zeros(n)

    for i, f in enumerate((261.63, 329.63, 392.00, 523.25)):
        off = n_samples(0.07 * i)
        m = n - off
        partial = sine(f, m) * exp_decay(m, 0.70, 0.012)
        partial += sine(f * 2.0, m) * exp_decay(m, 0.34, 0.012) * 0.30
        partial += sine(f * 3.01, m) * exp_decay(m, 0.18, 0.012) * 0.12
        out[off:] += partial * (0.70 - 0.09 * i)

    sub = sine(sweep(98.0, 65.4, n, 1.1), n) * adsr(n, 0.05, 0.35, 0.30, 1.30, 0.5) * 0.35
    air = highpass(white(n, rng), 6000.0) * exp_decay(n, 0.45, 0.030) * 0.07

    res = room_tail(soft_clip(out * 0.55 + sub + air, 1.1), 1.00, 0.32, rng)
    return normalize(fade(res, 4.0, 200.0), 0.84)


# ===========================================================================
# Recipes -- ambience
# ===========================================================================


def storm_loop(seconds: float = 12.0, cross: float = 2.0) -> tuple[np.ndarray, np.ndarray]:
    """
    Seamless stereo storm bed: wind, rain and a low swell over the sea.

    The loop is made seamless by generating ``seconds + cross`` of audio and
    cross-fading the overhang back over the head. Every modulator is aperiodic,
    so nothing has to line up on the boundary -- the wrap does that work.
    """
    name = "AMB_Storm_Loop"
    rng = rng_for(name)
    total = n_samples(seconds + cross)
    keep = n_samples(seconds)
    xf = n_samples(cross)

    channels = []
    for side in (0, 1):
        r = rng_for(f"{name}_{side}")

        # Wind: pink noise through a band-pass, breathing on a slow gust curve.
        w = pink(total, r)
        gust = np.clip(0.55 + 0.55 * wander(total, 0.30, r), 0.10, 1.5)
        wind = bandpass(w, 480.0, 0.55) * gust
        wind += bandpass(w, 1500.0, 0.9) * gust * 0.35
        wind *= 0.55

        # Rain: bright hiss with a gentle density wobble, plus scattered drops.
        rain = highpass(white(total, r), 2600.0)
        rain *= np.clip(0.70 + 0.30 * wander(total, 0.65, r), 0.25, 1.2)
        rain *= 0.22
        drops = np.zeros(total)
        for at in r.uniform(0.0, seconds + cross, 90):
            off = n_samples(float(at))
            if off >= total:
                continue
            m = min(total - off, n_samples(0.05))
            drops[off : off + m] += resonator(white(m, r), float(r.uniform(2600, 8200)), 12.0) \
                * exp_decay(m, 0.006, 0.0002) * float(r.uniform(0.05, 0.22))

        # Sea and structure: a low bed that keeps the mix from sounding thin.
        swell = lowpass(brown(total, r), 110.0, 0.7) * 0.55
        swell *= np.clip(0.65 + 0.35 * wander(total, 0.17, r), 0.20, 1.2)

        # Distant hull groans, so the station itself is audible in the weather.
        groan = np.zeros(total)
        for at in r.uniform(0.0, seconds + cross, 5):
            off = n_samples(float(at))
            m = min(total - off, n_samples(1.8))
            f = float(r.uniform(58.0, 105.0))
            groan[off : off + m] += sine(sweep(f, f * 0.82, m), m) \
                * adsr(m, 0.5, 0.4, 0.3, 0.6, 0.6) * 0.09

        mix = wind + rain + drops + swell + groan
        # Wrap the overhang back over the head to close the loop.
        looped = mix[:keep].copy()
        looped[:xf] = looped[:xf] * np.linspace(0.0, 1.0, xf) \
            + mix[keep : keep + xf] * np.linspace(1.0, 0.0, xf)
        # The loop is closed before the subsonic cut, so both ends get the same
        # filter state treatment and the seam stays inaudible.
        channels.append(dc_block(soft_clip(looped, 1.1)))

    left, right = channels
    peak = max(np.max(np.abs(left)), np.max(np.abs(right))) + 1e-12
    scale = 0.62 / peak
    return left * scale, right * scale


def storm_thunder() -> np.ndarray:
    """Distant thunder: a diffuse rumble that arrives without a sharp front."""
    name = "AMB_Storm_Thunder"
    rng = rng_for(name)
    n = n_samples(3.6)

    body = brown(n, rng)
    body = lowpass(body, 180.0, 0.7)
    body *= adsr(n, 0.12, 0.55, 0.40, 2.30, 0.55)

    crack = bandpass(white(n, rng), 900.0, 0.5) * exp_decay(n, 0.22, 0.030) * 0.22
    rumble = lowpass(white(n, rng), 90.0, 0.8) * adsr(n, 0.40, 0.70, 0.60, 1.60, 0.7) * 0.55
    rumble *= np.clip(0.75 + 0.25 * wander(n, 2.2, rng), 0.30, 1.3)

    res = room_tail(soft_clip(body * 0.9 + crack + rumble, 1.2), 1.40, 0.36, rng, damping=1400.0)
    return normalize(fade(res, 30.0, 400.0), 0.72)


# ===========================================================================
# Manifest
# ===========================================================================

SHAMBLER_FORMANTS = [(320.0, 6.0, 1.0), (860.0, 8.0, 0.55), (2100.0, 10.0, 0.20)]
SPRINTER_FORMANTS = [(620.0, 7.0, 1.0), (1700.0, 9.0, 0.70), (3400.0, 11.0, 0.35)]


def build_all() -> list[tuple[str, Path, float]]:
    results: list[tuple[str, Path, float]] = []

    mono = {
        "SFX_Weapon_Sidearm_Fire": sidearm_fire,
        "SFX_Weapon_Shotgun_Fire": shotgun_fire,
        "SFX_Weapon_Rifle_Fire": rifle_fire,
        "SFX_Weapon_Reload_Mag": reload_magazine,
        "SFX_Weapon_Reload_Shell": reload_shell,
        "SFX_Weapon_DryFire": dry_fire,
        "SFX_Weapon_Equip": weapon_equip,

        "SFX_Impact_Flesh": impact_flesh,
        "SFX_Impact_Critical": impact_critical,
        "SFX_Impact_World": impact_world,
        "SFX_Barricade_Repair": barricade_repair,

        "SFX_Enemy_Attack_Shambler": lambda: enemy_attack(
            "SFX_Enemy_Attack_Shambler", f0_from=96.0, f0_to=64.0, length=0.80,
            formants=SHAMBLER_FORMANTS, breath=0.30, swipe_hz=1400.0,
            swipe_gain=0.28, attack=0.030, tail_mix=0.26),
        "SFX_Enemy_Attack_Sprinter": lambda: enemy_attack(
            "SFX_Enemy_Attack_Sprinter", f0_from=280.0, f0_to=430.0, length=0.55,
            formants=SPRINTER_FORMANTS, breath=0.45, swipe_hz=3200.0,
            swipe_gain=0.34, attack=0.008, tail_mix=0.22),
        "SFX_Enemy_Attack_Brute": brute_attack,

        "SFX_Enemy_Death_Shambler": lambda: enemy_death(
            "SFX_Enemy_Death_Shambler", f0_from=88.0, f0_to=44.0, length=1.25,
            formants=SHAMBLER_FORMANTS, breath=0.35, collapse_hz=520.0, dissipate=0.22),
        "SFX_Enemy_Death_Sprinter": lambda: enemy_death(
            "SFX_Enemy_Death_Sprinter", f0_from=380.0, f0_to=120.0, length=1.05,
            formants=SPRINTER_FORMANTS, breath=0.50, collapse_hz=900.0, dissipate=0.30),
        "SFX_Enemy_Death_Brute": brute_death,

        "SFX_Player_Hurt": player_hurt,
        "SFX_Player_Down": player_down,
        "SFX_Player_LastStand": player_last_stand,

        "SFX_PowerUp_Pickup": powerup_pickup,
        "SFX_PowerUp_Drop": powerup_drop,
        "SFX_Purchase_Route": purchase_route,
        "SFX_Purchase_Weapon": purchase_weapon,
        "SFX_Purchase_Denied": purchase_denied,
        "SFX_Round_Start": round_start,
        "SFX_Round_Complete": round_complete,

        "AMB_Storm_Thunder": storm_thunder,
    }

    for name, recipe in mono.items():
        data = recipe()
        path = write_wav(name, data)
        results.append((name, path, len(data) / SR))
        print(f"  {name:32s} {len(data) / SR:5.2f}s  {path.stat().st_size / 1024:7.1f} KiB")

    left, right = storm_loop()
    path = write_wav("AMB_Storm_Loop", (left, right), stereo=True)
    results.append(("AMB_Storm_Loop", path, len(left) / SR))
    print(f"  {'AMB_Storm_Loop':32s} {len(left) / SR:5.2f}s  {path.stat().st_size / 1024:7.1f} KiB  (stereo)")

    return results


def main() -> int:
    print(f"[Ashfall] Generating audio into {OUT_DIR}")
    results = build_all()
    total_bytes = sum(p.stat().st_size for _, p, _ in results)
    print(f"[Ashfall] AUDIO_OK {len(results)} clips, "
          f"{sum(d for _, _, d in results):.1f}s, {total_bytes / 1024 / 1024:.2f} MiB")
    return 0


if __name__ == "__main__":
    sys.exit(main())
