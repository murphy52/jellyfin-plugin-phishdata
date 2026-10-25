#!/usr/bin/env python3
"""Build the local alignment corpus from the NAS library, using the NAS prior art.

Reads the NAS only: ffprobe for each file's chapters, and ffmpeg copying the first audio
stream over ssh (throttled with -readrate). Nothing is written on the NAS. Output stays out of
git and is consumed by CorpusTests (PHISHDATA_CORPUS=<corpus_dir>).

Usage:
  make_corpus.py <phish_core_dir> <corpus_dir> <library.tsv> [date ...]

<library.tsv> rows are "chapters<TAB>seconds<TAB>audio_streams<TAB>path", path relative to the
NAS library root. Per file, <corpus_dir>/<date>_<id>/ gets:
  audio.mka          the file's first audio stream, copied (kept so decoders can be compared)
  video_env.f32.gz   loudness envelope of that audio, as in the golden fixtures
  templates.f32.gz   every track's template envelope in track order (tracks without audio: none)
  show.json          tracks, the Python result (rows or skip reason) and the file's own chapters
The library's chapters were all written by the NAS scripts: "fleet" for the current phish_core,
"earlier" for runs of older versions. They measure agreement, not accuracy; there is no human truth.
A show whose show.json exists is skipped, so the script can be re-run after an interruption.
"""
import gzip
import hashlib
import json
import os
import re
import shlex
import subprocess
import sys
import threading
import time

core_dir, corpus, library = sys.argv[1:4]
only = set(sys.argv[4:])
sys.path.insert(0, core_dir)
import phish_core as pc  # noqa: E402

NAS = "nas"
ROOT = "/volume1/Movies/Concerts/Phish"
FLEET_LOG = "/home/Murphy52/phish-work/phish_fleet.log"
READRATE = 120  # x realtime: about 100 MB/s for a 1080p webcast, so playback on the NAS keeps the disk


def ssh(cmd, **kw):
    return subprocess.run(["ssh", "-o", "BatchMode=yes", NAS, cmd], check=True, **kw)


def parse_date(name):
    m = re.search(r"(19|20)(\d{2})[-.](\d{2})[-.](\d{2})", name)
    return f"{m[1]}{m[2]}-{m[3]}-{m[4]}" if m else None


def file_chapters(path):
    out = ssh(f"ffprobe -v error -show_chapters -of json {shlex.quote(path)} </dev/null", capture_output=True).stdout
    return [[c.get("tags", {}).get("title", ""), round(float(c["start_time"]), 2)] for c in json.loads(out)["chapters"]]


def copy_audio(path, dest):
    if os.path.exists(dest):
        return
    cmd = (f"nice -n 19 ffmpeg -nostdin -v error -readrate {READRATE} -i {shlex.quote(path)} "
           "-map 0:a:0 -vn -sn -dn -c copy -f matroska -")
    with open(dest + ".part", "wb") as f:
        ssh(cmd, stdout=f)
    os.replace(dest + ".part", dest)


fleet = set(re.findall(r"^DONE\s+\[(\d{4}-\d{2}-\d{2})\]", ssh(f"cat {FLEET_LOG}", capture_output=True, text=True).stdout, re.M))
shows = []
for line in open(library):
    nch, secs, _, rel = line.rstrip("\n").split("\t")
    date = parse_date(os.path.basename(rel))
    if date and float(secs) >= 600 and (not only or date in only):
        out = os.path.join(corpus, f"{date}_{hashlib.sha1(rel.encode()).hexdigest()[:6]}")
        shows.append((date, os.path.join(ROOT, rel.removeprefix("./")), int(nch), out))
print(f"{len(shows)} shows, {sum(os.path.exists(os.path.join(s[3], 'show.json')) for s in shows)} already done")


def prefetch(todo):
    # The NAS read is the slow part; copy the next show's audio while this one's templates download.
    for _, path, _, out in todo:
        os.makedirs(out, exist_ok=True)
        try:
            copy_audio(path, os.path.join(out, "audio.mka"))
        except subprocess.CalledProcessError as e:
            print(f"audio copy failed for {path}: {e}", flush=True)
            open(os.path.join(out, "audio.failed"), "w").close()


todo = [s for s in shows if not os.path.exists(os.path.join(s[3], "show.json"))]
threading.Thread(target=prefetch, args=(todo,), daemon=True).start()
for n, (date, path, nch, out) in enumerate(todo, 1):
    t0 = time.time()
    info = {"date": date, "source": os.path.relpath(path, ROOT), "fps": pc.FPS,
            "chapters_by": "fleet" if nch > 1 and date in fleet else "earlier" if nch > 1 else None,
            "file_chapters": file_chapters(path) if nch > 0 else []}
    tracks, reason = pc.get_tracks(date)
    if tracks is None:
        info["python"] = {"skip": reason}
    else:
        tmpls = []
        for t in tracks:
            tmpls.append(pc.decode_env(t["mp3_url"], pc.TMPL_SEC)[: pc.TMPL_SEC * pc.FPS] if t.get("mp3_url") else None)
            time.sleep(1)  # phish.in etiquette, as in the plugin
        audio = os.path.join(out, "audio.mka")
        while not os.path.exists(audio):
            if os.path.exists(os.path.join(out, "audio.failed")):
                sys.exit(f"stopping: no audio for {path}")
            time.sleep(2)
        venv = pc.decode_env(audio)
        vdur = len(venv) / pc.FPS
        total = sum((t.get("duration") or 0) for t in tracks) / 1000.0
        ratio = vdur / total if total else 0
        cache = {t["mp3_url"]: e for t, e in zip(tracks, tmpls) if e is not None}
        real_decode = pc.decode_env
        pc.decode_env = lambda src, max_sec=None: cache[src]
        if vdur < 600:
            result = {"skip": f"too-short ({pc.hms(vdur)})"}
        elif not pc.DUR_LO <= ratio <= pc.DUR_HI:
            result = {"skip": f"duration-mismatch video={pc.hms(vdur)} phishin={pc.hms(total)} (clip/partial?)"}
        else:
            rows, med, nlow, skip = pc.align(venv, vdur, tracks)
            result = {"skip": skip} if skip else {
                "median": round(med, 4), "low": nlow,
                "rows": [[r[0], round(r[1], 2), None if r[2] is None else round(r[2], 4)] for r in rows]}
            if not skip and med < pc.MIN_CONF_MEDIAN:
                result["skip"] = f"low-confidence median={med:.2f} (manual review)"
        pc.decode_env = real_decode
        info["python"] = result
        info["tracks"] = [{"title": t["title"], "set": t.get("set_name"), "duration_ms": t.get("duration"),
                           "frames": 0 if e is None else int(len(e))} for t, e in zip(tracks, tmpls)]
        with gzip.open(os.path.join(out, "video_env.f32.gz"), "wb") as f:
            f.write(venv.astype("<f4").tobytes())
        with gzip.open(os.path.join(out, "templates.f32.gz"), "wb") as f:
            for e in tmpls:
                if e is not None:
                    f.write(e.astype("<f4").tobytes())
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "show.json.part"), "w") as f:
        json.dump(info, f, indent=1)
    os.replace(os.path.join(out, "show.json.part"), os.path.join(out, "show.json"))
    p = info["python"]
    said = p.get("skip") or f"{len(p['rows'])} ch, median {p['median']:.2f}"
    print(f"[{n}/{len(todo)}] {date} {said}  ({info['chapters_by'] or 'no chapters'}, {time.time() - t0:.0f}s)", flush=True)
