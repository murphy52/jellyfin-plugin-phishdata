#!/usr/bin/env python3
"""Make a golden alignment fixture from a local COPY of a show, using the NAS prior art.

Reads the video and phish.in only; writes nothing next to the video.

Usage:
  make_alignment_fixture.py <phish_core_dir> <YYYY-MM-DD> <video_copy> <out_dir>

<phish_core_dir> is the folder holding phish_core.py (Ugreen_NAS/scripts). Needs numpy and
imageio-ffmpeg. Output (consumed by GoldenParityTests):
  video_env.f32.gz   video loudness envelope, little-endian float32, 20 fps
  templates.f32.gz   every track's template envelope, concatenated in track order
  show.json          track titles/durations/frame counts and the rows Python chose
"""
import gzip
import json
import os
import sys
import time

core_dir, date, video, out = sys.argv[1:5]
sys.path.insert(0, core_dir)
import phish_core as pc  # noqa: E402

os.makedirs(out, exist_ok=True)
tracks, reason = pc.get_tracks(date)
if tracks is None:
    sys.exit(f"cannot use {date}: {reason}")

t0 = time.time()
venv = pc.decode_env(video)
print(f"video envelope: {pc.hms(len(venv) / pc.FPS)} in {time.time() - t0:.1f}s")
tmpls = [pc.decode_env(t["mp3_url"], pc.TMPL_SEC)[: pc.TMPL_SEC * pc.FPS] if t.get("mp3_url") else None
         for t in tracks]

# Align with the templates already decoded, so the fixture and the alignment share one download.
cache = {t["mp3_url"]: e for t, e in zip(tracks, tmpls) if e is not None}
real_decode = pc.decode_env
pc.decode_env = lambda src, max_sec=None: cache[src]
rows, med, nlow, skip = pc.align(venv, len(venv) / pc.FPS, tracks)
pc.decode_env = real_decode
if skip:
    sys.exit(f"{date} does not align ({skip}); pick a show that does for a golden fixture")
if any(e is None for e in tmpls):
    sys.exit(f"{date} has tracks without audio; the fixture format assumes every track has a template")

with gzip.open(os.path.join(out, "video_env.f32.gz"), "wb") as f:
    f.write(venv.astype("<f4").tobytes())
with gzip.open(os.path.join(out, "templates.f32.gz"), "wb") as f:
    for e in tmpls:
        f.write(e.astype("<f4").tobytes())
with open(os.path.join(out, "show.json"), "w") as f:
    json.dump({
        "date": date,
        "fps": pc.FPS,
        "tracks": [{"title": t["title"], "set": t.get("set_name"), "duration_ms": t.get("duration"),
                    "frames": int(len(e))} for t, e in zip(tracks, tmpls)],
        "expected": [[r[0], round(r[1], 2), None if r[2] is None else round(r[2], 4)] for r in rows],
    }, f, indent=1)
print(f"{len(rows)} chapters, median {med:.2f}, {nlow} low-conf -> {out}")
