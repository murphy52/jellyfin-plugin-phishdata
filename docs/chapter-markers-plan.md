# Chapter markers for Phish videos: design and implementation plan

Issue: #26. Status: plan only, no feature code. Written 2026-10-22.

## 1. Recommendation in short

1. **Port the alignment to C# inside the plugin.** The NCC/consensus algorithm from `Ugreen_NAS/scripts/phish_core.py` is about 120 lines of numpy. Decoding uses the ffmpeg Jellyfin already ships (`IMediaEncoder.EncoderPath`). No Python, no sidecar, no NAS access, and it works for every user of the public plugin, not just David's NAS.
2. **Store chapters in Jellyfin's database through `IChapterManager.SaveChapters`, never in the media file (v1).** The plugin keeps its own copy of every result in its data folder, so it can put chapters back after a "Replace all metadata" refresh wipes them. A file-writing mode is left out of v1 on purpose (see §4.2).
3. **Trigger:** a queue fed by library events (only after Jellyfin has probed the file), plus a daily scheduled task that sweeps the backlog, retries pending shows, and re-applies cached chapters Jellyfin dropped. One show at a time, low process priority.
4. **Source audio:** phish.in API v2 per-track MP3s. Only the first 90 s of each track is streamed into ffmpeg, about 2–3 MB per track. Envelopes are cached so a show is never downloaded twice. Shows phish.in doesn't have yet (404, recent shows) are retried with backoff.
5. **Guardrails** carry over from the NAS fleet (duration ratio, anchor quorum, median confidence). Shows that fail them become **Needs review** on the plugin settings page, with Retry and Apply anyway buttons. A `<video>.chapters.txt` sidecar is the manual override.

## 2. What exists today

**Plugin.** `PhishNetMovieProvider` identifies a show from the filename and sets `PremiereDate`, the `PhishNet` provider ID and the `PhishCollection*` provider IDs (`Services/PhishCollectionMetadata.cs`). `PhishCollectionLibraryHandler` already subscribes to `ILibraryManager.ItemAdded/ItemUpdated`. `Plugin.DataFolderPath` gives a writable folder. Settings live in `Configuration/PluginConfiguration.cs` plus `configPage.html`.

**Prior art** (`Ugreen_NAS/scripts/phish_core.py`, read-only for this work):
- `decode_env`: ffmpeg → mono 8 kHz float → 50 ms RMS frames → `log1p(rms*20)`, giving a 20 fps loudness envelope.
- `ncc_search`: FFT normalized cross-correlation, with a guard that zeroes digitally silent windows (fixes false matches at setbreaks).
- `align`: an independent global match per track (first 90 s of each MP3); a Theil–Sen line `pos = a + b·cumulative_time` over anchors (NCC ≥ 0.45, slope clamped to 0.85–1.25); a quorum `max(3, n/4)` within 90 s of the line; then a ±150 s refine per track, keeping the fitted time when refine NCC < 0.30.
- `process_show`: guards (phish.in `audio_status == complete`, video/phish.in duration ratio 0.6–1.5, median NCC ≥ 0.40), then an in-place remux.

**Fleet results on the NAS** (`/home/Murphy52/phish-work/phish_fleet.log`, read-only): 22 DONE across runs, and most of the library was already chaptered. Skips: 15 no-audio, 12 no-date, 8 no-consensus, 6 HTTP 404 (show not on phish.in yet; later runs succeeded), 4 too-short, 3 clip/partial, 2 low-confidence. One no-consensus case (2026-07-21) came from a **setbreak step**: the webcast's offset jumped from +64 s in set 1 to +166 s in set 2, which a single line fits poorly (residuals up to 52 s). It later passed after the slope fix. Today, 103 show files over 1 GB live under `/Concerts/Phish`.

**Measured on the M6, read-only, on a copy** (2026-07-07, 3h07m, 9.6 GB MKV, warm page cache):

| Step | Time |
|---|---|
| phish.in show JSON | 0.2 s |
| Video audio → envelope (225,239 frames) | 5.5 s |
| 18 MP3 templates (90 s each, streamed) | 5.5 s |
| Alignment (NCC + fit + refine) | 0.5 s |

Result: 18 chapters, median NCC 0.73, none low, matching the fleet's output for that show. Peak RSS was **770 MB**, because Python buffers the whole 3-hour PCM (8 kHz × 4 B × 11,262 s ≈ 360 MB, plus copies). The C# port must build the envelope while streaming. The cost on the NAS is dominated by one sequential read of the video file (about 1 min for 9.6 GB from HDD); decode and math are seconds. Cached envelope fixtures for this show are about 0.9 MB gzipped.

## 3. Jellyfin 12 facts that shape the design

Checked against Jellyfin `v12.3` source and the `Jellyfin.Controller 12.0.0` package the plugin builds against:

- `IChapterManager.SaveChapters(BaseItem, IReadOnlyList<ChapterInfo>)` replaces the item's chapters in the DB. It **drops chapters whose start is at or past `item.RunTimeTicks`**, so the item must be probed first.
- `ProbeProvider` (`FFProbeVideoInfo.Fetch`) saves chapters read from the file **whenever it runs in Default or FullRefresh mode**, which overwrites anything the plugin saved. It runs on first scan, when the file's mtime changes, when media info is missing, and on "Replace all metadata". A plain "Scan library" leaves DB chapters alone.
- If the server setting `DummyChapterDuration > 0` and the file has ≤ 1 chapter, the probe writes **dummy chapters** at fixed intervals named "Chapter N" (localized). Those count as "missing" for our purpose.
- `NormalizeChapterNames` renames empty or timestamp-looking names to "Chapter N".
- `ItemAdded` fires before the first probe (no runtime or chapters yet), and `ItemUpdated` fires after a refresh saves. Saving chapters does **not** raise `ItemUpdated`, so writing them can't loop.
- Chapter thumbnails come from Jellyfin's own "Extract chapter images" task or library option. The plugin doesn't need to generate them.

## 4. Decisions

### 4.1 Where the work runs

| Option | For | Against |
|---|---|---|
| **A. C# port in the plugin (chosen)** | Zero extra infra; works for all plugin users; uses Jellyfin's ffmpeg; can read and write Jellyfin's chapter DB directly; measured cost is small | Must re-implement and keep parity with the Python (FFT, Theil–Sen); runs inside the Jellyfin process |
| B. Plugin triggers the Python on the NAS | Reuses tested code | The plugin runs in a container: it would need the Docker socket or SSH from inside Jellyfin (a security hole) and only works on David's NAS |
| C. Sidecar HTTP service | Isolates CPU, reuses Python | Another container to deploy and secure; David-only; the plugin still has to write chapters to Jellyfin |

Cost per show under A: one sequential read of the video plus about 18–30 partial MP3 fetches (about 40–70 MB down), seconds of CPU. Run ffmpeg at `BelowNormal` priority and process one show at a time. The aligner sits behind an `IChapterAligner` interface, so B or C could be added later without touching the Jellyfin side.

### 4.2 Where chapters are stored

**Chosen: Jellyfin DB via `IChapterManager`, with a plugin-side cache that re-applies.**
- No risk to David's files, nothing to recover if alignment is wrong, and a wrong result is fixed by re-running.
- Downside: chapters live only in Jellyfin, so they don't show in VLC or Plex, and a "Replace all metadata" or a file change wipes them. Mitigation: the plugin stores each result in `DataFolderPath/chapters/<itemId>.json` with a file fingerprint (path, size, mtime). After any `ItemUpdated`, if the fingerprint still matches and the DB chapters aren't ours, it re-saves them. No new alignment, no downloads. If the fingerprint changed, the file is new: discard the result and re-align.

**Rejected for v1: rewriting the media file in place**, which is what the NAS scripts do.
- The Jellyfin container mounts `/Concerts` read-write as PUID 1001, so it *could*. But it adds a 9 GB temp copy per show, needs write access to the directory, and turning `.mp4` into `.mkv` changes the path, which in Jellyfin means a new item with lost watch state. A bug would damage originals across many users' libraries.
- The NAS `phish-chapter.py` stays the tool for when David wants chapters embedded in the file. The plugin respects file chapters it finds (it skips the show).
- A future opt-in "embed in file" mode can reuse the NAS safety recipe (remux to a temp file in the same directory, verify, atomic replace, same container/extension only). It is out of scope here and flagged in §9.

### 4.3 Trigger, detection, idempotency

**Queue worker.** A singleton `IHostedService` owns a `Channel<Guid>` that dedups item IDs and processes one at a time.
- `ItemUpdated` enqueues a Phish movie (has the `PhishNet` provider ID and `PremiereDate`) once it has `RunTimeTicks > 0`. That means the probe has run and our chapters will survive `SaveChapters`' runtime filter.
- `ItemAdded` is ignored, since nothing is probed yet. The `ItemUpdated` that follows the first refresh covers new files.
- The event handler only enqueues; all I/O happens on the worker.

**Scheduled task** "Phish: add chapter markers" (`IScheduledTask`, daily at 03:00 by default, plus Run now):
- Re-applies cached chapters the DB lost.
- Retries pending items whose backoff has expired.
- Sweeps every Phish movie with no state file, capped by "max shows per run" (default 10) so the first run on a big library is spread over several nights.

**"Missing chapters"** means `GetChapters(id)` returns ≤ 1 chapter, **or** the chapters look exactly like Jellyfin's dummy chapters (start times `i × DummyChapterDuration` and default names). Real chapters with real names (from the file, from the NAS fleet, or from a rip) are never replaced.

**Idempotency** comes from the per-item state file:
- `status` ∈ {Applied, Pending, NeedsReview, Failed, Skipped}
- `fingerprint`
- `algorithmVersion`
- `attempts`, `nextAttemptUtc`
- `showDate`
- `phishInUpdatedAt`
- the chapter rows with per-song confidence
- the reason for the status

Rules:
- An Applied item whose fingerprint still matches is never re-aligned.
- Skipped items (no date, clip/partial, no-audio-forever) are only retried when the fingerprint or `algorithmVersion` changes.
- Writes are atomic (temp file + rename).

### 4.4 Source audio, rate limits, thresholds

- **phish.in API v2**, `GET /api/v2/shows/{yyyy-MM-dd}`. Use tracks with `mp3_url` and `duration`; drop `set_name == "Soundcheck"` and titles starting `(Check)`. Chapter titles come from phish.in track titles, which match the audio being aligned. Phish.net isn't needed for this feature.
- Send a descriptive User-Agent (`Jellyfin-PhishData/<version>`). The NAS notes say phish.in returned 403 to Python's default UA. A plain curl got 200 today, but a UA is required for politeness either way.
- **Etiquette:**
  - One request at a time.
  - Stream each MP3 into ffmpeg's stdin and stop reading once `-t 90` is satisfied (as the current core does); hard cap of 8 MB per track.
  - Honor `429`/`Retry-After`.
  - Cache each template envelope (1,800 floats, about 7 KB) under `DataFolderPath/phishin/<sha1(mp3_url)>.f32`, so re-alignment never re-downloads.
- **Not on phish.in yet** (404, or `audio_status != complete`): status Pending, retried at 1 h, 6 h, 24 h, then daily for 14 days, then weekly up to 60 days, then Skipped("no-audio"). Recent tour shows usually appear within days.
- **Thresholds** stay constants in code, kept identical to the prior art for parity, and are not user settings:

| Constant | Value |
|---|---|
| Template length | 90 s |
| Anchor | NCC ≥ 0.45 |
| Slope clamp | 0.85–1.25 |
| Quorum | `max(3, n/4)` within 90 s |
| Refine | ±150 s, accept if ≥ 0.30 |
| Median gate | ≥ 0.40 |
| Duration ratio | 0.6–1.5 |
| Minimum video length | 10 min |

- **Manual fallback, in order of effort:**
  1. **Needs review list** on the settings page: date, file, reason, median NCC, and how many songs were low-confidence. Buttons: **Retry**, **Apply anyway** (only when alignment produced rows, i.e. gated only by the median), **Ignore**.
  2. **Sidecar override:** if `<video basename>.chapters.txt` exists next to the video, with one `H:MM:SS Title` per line, the plugin applies it verbatim and never aligns that item. It's durable, editable from the NAS, and survives Jellyfin rebuilds.
  3. The NAS `phish-align.py` stays the debugging tool: it prints the per-song table without changing anything.

### 4.5 Settings

Added to `PluginConfiguration` and the settings page:
- `EnableChapterMarkers` (default **false**: the feature reads whole video files and downloads from phish.in, so other users of the public plugin opt in)
- `ChapterMarkersOnLibraryChanges` (default true; when false, only the scheduled task runs)
- `ChapterMarkersMaxShowsPerRun` (default 10)
- `ReplaceDummyChapters` (default true)

Plus a read-only status table (counts by status, and the Needs review list with actions), served by a small `[Authorize(Policy = "RequiresElevation")]` API controller.

### 4.6 Logging and failure handling

- One Information line per show, the same shape as the fleet log: `Chapters DONE 2026-07-07 "ph2026-07-07…mkv": 18 ch, median 0.73, 0 low (read 61s, fetch 6s, align 0.5s)`. SKIP, PENDING and REVIEW lines include the reason. The per-song table goes to Debug.
- Every item runs inside try/catch. An exception sets Failed with backoff (max 5 attempts, then NeedsReview) and never stops the worker.
- ffmpeg runs with a timeout (30 min video, 2 min per MP3) and is killed on cancellation (server shutdown, task cancel). Temp files are only envelope caches and are written atomically.
- The plugin never opens a media file for writing in v1.

## 5. Architecture

```
ItemUpdated ──► ChapterQueue (Channel<Guid>, dedup) ◄── ChapterMarkerTask (daily / Run now)
                      │
                      ▼
              ChapterMarkerService.ProcessAsync(item)
                ├─ ChapterStateStore        (DataFolderPath/chapters/<id>.json)
                ├─ ChapterDetector          (missing? dummy? ours?)
                ├─ SidecarChapterReader     (<name>.chapters.txt override)
                ├─ PhishInClient            (show JSON, MP3 stream, backoff)
                ├─ EnvelopeDecoder          (ffmpeg via IMediaEncoder.EncoderPath, streaming)
                ├─ TemplateCache            (DataFolderPath/phishin/*.f32)
                ├─ ConsensusAligner         (pure C#: Envelope, Ncc/Fft, TheilSen)
                └─ IChapterManager.SaveChapters
```

New files go under `Jellyfin.Plugin.PhishNet/Chapters/`. `ConsensusAligner`, `Ncc`, `Fft` and `EnvelopeBuilder` have **no Jellyfin dependencies**, so they're unit-testable and match the Python 1:1.

**FFT:** a self-contained iterative radix-2 real FFT (about 80 lines), sized to the next power of two ≥ video+template frames (2^18 for a 3 h show). No new NuGet dependency, so the release zip stays DLL + PDB only. MathNet.Numerics was considered and rejected because it would have to ship in the zip.

**Envelope while streaming:** read ffmpeg's `f32le` stdout in 400-sample hops (8000/20) and emit `log1p(sqrt(mean(x²)+1e-9)·20)` per hop. Memory is O(frames): about 0.9 MB for 3 h, not 360 MB of PCM.

## 6. Algorithm plan

**Phase 1 is a parity port**: same constants and same steps as `phish_core.align` at the version captured in the golden fixture. Acceptance: on the golden fixtures, every start time is within 0.1 s (2 frames) of Python and every NCC is within 0.01.

**Phase 2 improvements**, each judged on the fixture corpus (§7). Adopt only if no show gets worse:
- **Per-set piecewise fit:** fit the intercept per `set_name` (shared slope), falling back to the global line when a set has fewer than 2 anchors. Targets setbreak trims like 2026-07-21.
- **Order constraint in refine:** already partly there via `prev + MIN_GAP`. Add a check that the final starts are strictly increasing, and flag rather than silently reorder.
- **Encores and very short tracks** (< 90 s), where the template is the whole track: keep, and mark as low-confidence if refine < 0.30.

Bumping `algorithmVersion` makes the task re-align NeedsReview and Skipped items only. Applied items are left alone unless David triggers Retry.

## 7. Testing

The test project already has pre-existing failures; compare against a recorded baseline rather than expecting green.

| Layer | Test | Data |
|---|---|---|
| `Fft`/`Ncc` | Matches naive O(n·L) NCC on random data to 1e-5; silent-window guard zeroes digital silence; `lo/hi` window bounds | Generated in-test |
| `EnvelopeBuilder` | Hop math, partial last hop dropped, known sine RMS | Generated PCM |
| `ConsensusAligner` | Synthetic shows: random "song" envelopes with gaps; drift slopes 0.95–1.08; a setbreak step; a missing track; a wrong track; all-silence; too few anchors → `few-anchors`; no agreement → `no-consensus` | Generated in-test, seeded |
| Golden parity | C# output vs Python's on real envelopes | `Fixtures/align/2026-07-07/` (video envelope + templates gz, ≈0.9 MB) made by a fixture script that imports `phish_core` |
| `PhishInClient` | Parse, soundcheck filter, 404/`audio_status`→Pending, 429 Retry-After, UA header | Recorded JSON + fake `HttpMessageHandler` |
| `EnvelopeDecoder` | Decodes a 5 s `lavfi` sine; cancellation kills the process | Skipped when no ffmpeg is on PATH |
| `ChapterDetector` | ≤ 1 chapter, dummy pattern, real chapters, our own chapters | In-memory `ChapterInfo` lists |
| `ChapterStateStore` | Atomic write/read, fingerprint mismatch, backoff schedule | Temp dir |
| `ChapterMarkerService` | Orchestration with fakes: applies, re-applies after a wipe, never re-aligns Applied, honors the sidecar | Moq on `IChapterManager`, `ILibraryManager` |

No test downloads a show or reads a real video. A local-only `Category=Corpus` test can run C# vs Python over envelopes for every show the fleet chaptered. Those envelopes are generated on the M6 from copies, kept out of git, and the test is skipped when absent.

**End-to-end, before any release:** run a throwaway Jellyfin 12 container on the M6 with a copy of 2–3 shows (one good, one 404-pending, one clip). Check that chapters appear in the web player, survive a library scan, come back after "Replace all metadata", and that the Needs review page works. Never against David's live server until he chooses to install the build.

## 8. Implementation phases (TDD per phase)

0. **Environment.** Install the .NET 10 SDK on the M6 in `~/.dotnet10` (user-local, as on the M1 Pro); none is installed now. Record the current test baseline. Add `tools/make_alignment_fixture.py` and generate the 2026-07-07 golden fixture from a copy.
1. **Core port:** `Fft`, `Ncc`, `EnvelopeBuilder`, `TheilSen`, `ConsensusAligner`, with unit, synthetic and golden-parity tests.
2. **I/O adapters:** `EnvelopeDecoder` (streaming, priority, timeout, cancel), `PhishInClient`, `TemplateCache`.
3. **Jellyfin integration:**
   - `ChapterStateStore`, `ChapterDetector`, `ChapterMarkerService`, `ChapterQueue` (hosted service)
   - the hook in the existing library handler (or a sibling handler)
   - `ChapterMarkerTask`
   - config properties
   - DI registration in `PluginServiceRegistrator`
4. **Manual path:** sidecar reader, settings-page status table, Retry / Apply anyway / Ignore via the API controller.
5. **Validation:**
   - corpus parity run on the M6
   - Phase-2 algorithm tweaks, kept only if the corpus improves
   - throwaway-Jellyfin E2E
   - README section
6. **Release:** separate decision for David (§9). Suggest `manifest-dev.json` first.

Each phase is its own PR off `master`, small enough to review.

## 9. Needs David

- **(1, merges and releases)** Merging and releasing the eventual feature PRs, and whether the first build goes to `manifest-dev.json` only. Recommendation: dev manifest first, install on his server, watch one tour's worth of new shows. Blocks only the release, not development.
- **(7, product direction)** Whether he ever wants an "embed chapters in the file" mode in the plugin. Recommendation: no for now; keep that on the NAS scripts. Blocks nothing in v1.

## 10. Review log

(Filled in after the independent review.)
