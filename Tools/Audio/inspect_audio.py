#!/usr/bin/env python3
"""
Ashfall audio inspection: numbers first, then a contact sheet.

The build machine for this project has no audio device, so "does it sound
right" has to be answered by looking at the signal rather than listening to it.
This prints per-clip peak/RMS/crest/centroid and writes a PNG contact sheet of
log-frequency spectrograms so a gunshot that came out as silence, a clip that
is clipping, or a "sound" that is really just broadband hiss is visible at a
glance.

    /usr/bin/python3 Tools/Audio/inspect_audio.py [--png /tmp/ashfall-audio.png]

Nothing here is needed to build or run the game; it is a verification tool.
"""

from __future__ import annotations

import argparse
import math
import struct
import sys
import wave
import zlib
from pathlib import Path

import numpy as np

REPO_ROOT = Path(__file__).resolve().parents[2]
AUDIO_DIR = REPO_ROOT / "Assets" / "Ashfall" / "Audio"


def read_wav(path: Path) -> tuple[np.ndarray, int]:
    with wave.open(str(path), "rb") as w:
        channels, width, rate = w.getnchannels(), w.getsampwidth(), w.getframerate()
        frames = w.readframes(w.getnframes())
    if width != 2:
        raise ValueError(f"{path.name}: expected 16-bit PCM, got {width * 8}-bit")
    data = np.frombuffer(frames, dtype="<i2").astype(np.float64) / 32768.0
    if channels > 1:
        data = data.reshape(-1, channels).mean(axis=1)
    return data, rate


def spectral_centroid(x: np.ndarray, rate: int) -> float:
    spectrum = np.abs(np.fft.rfft(x * np.hanning(len(x))))
    freqs = np.fft.rfftfreq(len(x), 1.0 / rate)
    total = spectrum.sum()
    return float((spectrum * freqs).sum() / total) if total > 1e-12 else 0.0


def spectrogram(x: np.ndarray, rate: int, cols: int, rows: int) -> np.ndarray:
    """Log-frequency, dB-scaled magnitude, resampled to a fixed tile size."""
    win = 2048
    hop = max(1, (len(x) - win) // max(cols, 1)) if len(x) > win else 1
    frames = []
    for start in range(0, max(1, len(x) - win), hop):
        seg = x[start : start + win]
        if len(seg) < win:
            seg = np.pad(seg, (0, win - len(seg)))
        frames.append(np.abs(np.fft.rfft(seg * np.hanning(win))))
    if not frames:
        return np.zeros((rows, cols))

    mag = np.array(frames).T  # (freq, time)
    freqs = np.fft.rfftfreq(win, 1.0 / rate)

    # Log-spaced frequency bands, 40 Hz to Nyquist: linear bins waste 90% of
    # the picture on content nothing in this game lives in.
    edges = np.geomspace(40.0, rate / 2.0, rows + 1)
    banded = np.zeros((rows, mag.shape[1]))
    for r in range(rows):
        sel = (freqs >= edges[r]) & (freqs < edges[r + 1])
        if sel.any():
            banded[r] = mag[sel].max(axis=0)
        elif r > 0:
            banded[r] = banded[r - 1]

    db = 20.0 * np.log10(banded + 1e-9)
    db = np.clip((db - db.max() + 72.0) / 72.0, 0.0, 1.0)

    idx = np.clip((np.arange(cols) * db.shape[1] / cols).astype(int), 0, db.shape[1] - 1)
    return db[::-1, idx]


def write_png(path: Path, rgb: np.ndarray) -> None:
    """Minimal PNG writer: no image library on this machine, and none needed."""
    height, width, _ = rgb.shape
    raw = b"".join(b"\x00" + rgb[y].tobytes() for y in range(height))

    def chunk(tag: bytes, payload: bytes) -> bytes:
        return (struct.pack(">I", len(payload)) + tag + payload
                + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 6))
    png += chunk(b"IEND", b"")
    path.write_bytes(png)


def colourise(v: np.ndarray) -> np.ndarray:
    """Dark teal to amber, matching the game's own palette."""
    r = np.clip(1.35 * v**1.4, 0, 1)
    g = np.clip(0.35 * v + 0.75 * v**2.2, 0, 1)
    b = np.clip(0.55 * v**0.6 - 0.45 * v**2.5, 0, 1)
    return (np.stack([r, g, b], axis=-1) * 255).astype(np.uint8)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--png", default="/tmp/ashfall-audio.png")
    args = parser.parse_args()

    paths = sorted(AUDIO_DIR.glob("*.wav"))
    if not paths:
        print(f"No WAVs under {AUDIO_DIR}", file=sys.stderr)
        return 1

    tile_w, tile_h, pad = 132, 76, 4
    cols = 5
    rows = math.ceil(len(paths) / cols)
    sheet = np.zeros((rows * (tile_h + pad) + pad, cols * (tile_w + pad) + pad, 3), np.uint8)
    sheet[:] = (10, 12, 15)

    print(f"{'clip':34s} {'sec':>6s} {'peak':>6s} {'rms':>7s} {'crest':>6s} {'centroid':>9s}  flags")
    problems = 0
    for i, path in enumerate(paths):
        x, rate = read_wav(path)
        peak = float(np.max(np.abs(x))) if len(x) else 0.0
        rms = float(np.sqrt(np.mean(x**2))) if len(x) else 0.0
        crest = peak / rms if rms > 1e-9 else 0.0
        centroid = spectral_centroid(x, rate)
        dc = float(np.mean(x))

        flags = []
        if peak < 0.20:
            flags.append("QUIET")
        if peak > 0.999:
            flags.append("CLIPPED")
        if rms < 1e-4:
            flags.append("SILENT")
        if abs(dc) > 0.01:
            flags.append("DC-OFFSET")
        if not np.all(np.isfinite(x)):
            flags.append("NON-FINITE")
        problems += len(flags)

        print(f"{path.stem:34s} {len(x)/rate:6.2f} {peak:6.3f} {rms:7.4f} "
              f"{crest:6.2f} {centroid:8.0f}Hz  {' '.join(flags)}")

        tile = colourise(spectrogram(x, rate, tile_w, tile_h))
        y0 = pad + (i // cols) * (tile_h + pad)
        x0 = pad + (i % cols) * (tile_w + pad)
        sheet[y0 : y0 + tile_h, x0 : x0 + tile_w] = tile

    write_png(Path(args.png), sheet)
    print(f"\nContact sheet: {args.png}  ({len(paths)} clips, {rows}x{cols})")
    print("AUDIO_INSPECT_OK" if problems == 0 else f"AUDIO_INSPECT_FLAGS={problems}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
