# Chapter markers for Phish videos: design and implementation plan

Issue: #26. Status: plan only, no feature code. Written 2026-10-22.

## 1. Recommendation in short

1. **Port the alignment to C# inside the plugin.** The NCC/consensus algorithm from `Ugreen_NAS/scripts/phish_core.py` is about 120 lines of numpy. Decoding uses the ffmpeg Jellyfin already ships (`IMediaEncoder.EncoderPath`). No Python, no sidecar, no NAS access, and it works for every user of the public plugin, not just David's NAS.
2. **Store chapters in Jellyfin's database through `IChapterManager.SaveChapters`, never in the media file (v1).** The plugin keeps its own copy of every result in a stable data folder. After a "Replace all metadata" refresh wipes the chapters, it puts them back, but only where the item then has no chapters or Jellyfin's dummy ones. A file-writing mode is left out of v1 on purpose (see §4.2).
3. **Trigger:** library events (only after Jellyfin has probed the file, debounced until the file stops changing), plus a daily scheduled task that sweeps the backlog, retries pending shows, and re-applies cached chapters Jellyfin dropped. Both paths share one service with a global one-show-at-a-time lock. The task awaits its own work, so cancellation and progress are real.
4. **Source audio:** phish.in API v2 per-track MP3s. Only the first 90 s of each track is streamed into ffmpeg, about 2–3 MB per track. Envelopes are cached so a show is never downloaded twice. Shows phish.in doesn't have yet (404, recent shows) are retried with backoff.
5. **Guardrails** carry over from the NAS fleet (duration ratio, anchor quorum, median confidence). Shows that fail them become **Needs review** on the plugin settings page, with Retry and Apply anyway buttons. A `<video>.chapters.txt` sidecar is the manual override.

## 2. What exists today

**Plugin.** `PhishNetMovieProvider` identifies a show from the filename. With Phish.net data it sets `PremiereDate`, the `PhishNet` provider ID and the `PhishCollection*` provider IDs (`Services/PhishCollectionMetadata.cs`). Without it, the basic-metadata fallback (`PopulateBasicMetadata`) sets only `PremiereDate`, so chapter eligibility can't depend on the provider IDs. `PhishCollectionLibraryHandler` already subscribes to `ILibraryManager.ItemAdded/ItemUpdated`. `Plugin.DataFolderPath` gives a writable folder, but its name includes the plugin version (`phishnet_1.6.1`), so anything stored there is lost on every upgrade. Settings live in `Configuration/PluginConfiguration.cs` plus `configPage.html`.

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
- `SaveChapters` doesn't validate rows beyond the runtime filter: no sorting, and negative or duplicate starts get through. The plugin must validate every chapter list it saves.
- `SaveChapters` doesn't extract or clean chapter images. Thumbnails for the new chapters show up only after Jellyfin's "Extract chapter images" task runs (or the library option extracts during scans). Images left over from replaced dummy chapters are Jellyfin's to clean up; the E2E run checks how that looks.

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
- Downside: chapters live only in Jellyfin, so they don't show in VLC or Plex, and a "Replace all metadata" or a file change wipes them.
- Mitigation: the plugin stores each result in `<data>/chapters/<itemId>.json` with a file fingerprint (path, size, mtime). `<data>` is a **stable, unversioned** folder (`ApplicationPaths.DataPath/phishdata`), not `Plugin.DataFolderPath`, which moves on every version bump. Each record carries a `schemaVersion` so later formats can migrate it.
- **Re-apply rule.** After an `ItemUpdated` with a matching fingerprint:
  - If the DB has **zero chapters or a positively identified dummy set**, re-save the cached chapters. No new alignment, no downloads.
  - If the DB holds our cached chapters, do nothing.
  - If the DB holds *other* real chapters (from the file, another plugin or a person), leave them alone and mark the record **Superseded**. The plugin never overwrites real chapters, because `ChapterInfo` has no owner field to prove they are ours.
- If the fingerprint changed, the file is new: discard the result and re-align.

**Rejected for v1: rewriting the media file in place**, which is what the NAS scripts do.
- The Jellyfin container mounts `/Concerts` read-write as PUID 1001, so it *could*. But it adds a 9 GB temp copy per show, needs write access to the directory, and turning `.mp4` into `.mkv` changes the path, which in Jellyfin means a new item with lost watch state. A bug would damage originals across many users' libraries.
- The NAS `phish-chapter.py` stays the tool for when David wants chapters embedded in the file. The plugin respects file chapters it finds (it skips the show).
- A future opt-in "embed in file" mode can reuse the NAS safety recipe (remux to a temp file in the same directory, verify, atomic replace, same container/extension only). It is out of scope here and flagged in §9.

### 4.3 Trigger, detection, idempotency

**Eligibility.** A movie is a Phish show for chaptering when `PhishFileNameParser` (`LooksPhishRelated`, then `Parse`) yields a show date from its title or path. This works with or without a Phish.net key. If `PremiereDate` is set and disagrees with the parsed date, the item goes to Needs review instead of guessing.

**One service, two entry points.** `ChapterMarkerService.ProcessAsync(item, ct)` does all the work. A process-wide `SemaphoreSlim(1)` keeps it to one show at a time, and an in-flight set holds each item ID until its processing fully finishes, so an event and the task can't run the same item twice.
- **Library events.** A singleton `ChapterQueue` (registered once and exposed both as itself and as `IHostedService`) takes item IDs from `ItemUpdated` for eligible movies with `RunTimeTicks > 0`. That means the probe has run and our chapters will survive `SaveChapters`' runtime filter.
  - **Debounce:** an item is processed only after its file mtime is at least 10 minutes old and no further event arrived for it. That covers copies in progress and the burst of updates during a scan.
  - `ItemAdded` is ignored, since nothing is probed yet. The `ItemUpdated` after the first refresh covers new files.
  - The event handler only enqueues; all I/O happens on the worker.

**Scheduled task** "Phish: add chapter markers" (`IScheduledTask`; default trigger daily at 04:00, which the admin can change in Jellyfin's task settings, plus Run now). It calls the service directly and **awaits** each item, reporting progress and honoring its cancellation token, so Run now stays active until the work is done. It:
- Re-applies cached chapters the DB lost.
- Retries pending items whose backoff has expired.
- Sweeps every eligible movie with no state file, stopping at whichever budget runs out first: "max shows per run" (default 10) or "max GB read per run" (default 100). A first run on a big library is spread over several nights.

**"Missing chapters"** means `GetChapters(id)` returns **zero** chapters, **or** (when `ReplaceDummyChapters` is on) the list positively matches Jellyfin's dummy signature: starts exactly `i × DummyChapterDuration` with the localized default "Chapter N" names, for the current server setting. A single real chapter, or any real chapters (from the file, the NAS fleet or a rip), are never replaced.

**Idempotency** comes from the per-item state file:
- `status` ∈ {Applied, Superseded, Pending, NeedsReview, Failed, Skipped, Ignored}
- `fingerprint` (path, size, mtime), plus the sidecar's content hash when one exists
- `schemaVersion`, `algorithmVersion`
- `attempts`, `nextAttemptUtc`
- `showDate`
- the chapter rows with per-song confidence
- the reason for the status

Rules:
- The sidecar check (§4.4) runs **before** the Applied fast path, and the sidecar hash is part of the fingerprint, so adding or editing a sidecar takes effect.
- An Applied item whose fingerprint still matches is never re-aligned.
- Skipped items (no date, clip/partial, no-audio-forever) are only retried when the fingerprint or `algorithmVersion` changes.
- **Ignored** (set by the admin) only changes on an explicit Retry or when the media file or sidecar changes. An algorithm bump never touches it.
- **File stability:** stat the file before decoding, after decoding and right before `SaveChapters`. If size or mtime moved, discard the result and leave the item Pending.
- **Order of writes:** save chapters to the DB first, then write the state file. A crash in between leaves chapters in the DB and no record; the next pass finds real chapters equal to a fresh alignment's output, or simply re-aligns. Either way no data is lost.
- State writes are atomic (temp file + rename).

### 4.4 Source audio, rate limits, thresholds

- **phish.in API v2**, `GET /api/v2/shows/{yyyy-MM-dd}`. Drop only soundcheck tracks (`set_name == "Soundcheck"`, titles starting `(Check)`) and keep **every other track in position order**, because each track's duration builds the cumulative timeline. A track with a duration but no MP3 still gets a fitted chapter (as in the prior art). A missing or zero duration sends the show to Needs review rather than silently shifting every later song. Chapter titles come from phish.in track titles, which match the audio being aligned. Phish.net isn't needed for this feature.
- Send a User-Agent naming the project and its URL (`Jellyfin-PhishData/<version> (+https://github.com/murphy52/jellyfin-plugin-phishdata)`). The NAS notes say phish.in returned 403 to Python's default UA. A plain curl got 200 today, but a UA is required for politeness either way.
- **Etiquette:**
  - One request at a time, process-wide, at least 1 s apart with jitter.
  - Stream each MP3 into ffmpeg's stdin and stop reading once `-t 90` is satisfied (as the current core does).
  - Cap 8 MB per track and 150 MB per show, with at most 3 redirects.
  - A template that hits the byte cap before 90 s of audio is decoded is rejected, unless the track itself is shorter than 90 s.
  - Honor `Retry-After` on 429 and 503, and back off on other 5xx.
- **Template cache:** one self-describing record per track under `<data>/phishin/<sha1(mp3_url)>.bin`:
  - The header holds the cache format version, the envelope version (sample rate, FPS, formula), the frame count and the source URL.
  - On read, check the header and that every sample is finite; on any mismatch, re-fetch.
  - phish.in MP3 URLs are content-addressed blob keys (`/blob/<key>.mp3`), so new audio arrives under a new URL. Keying by URL is enough, and no ETag revalidation is needed.
  - Re-alignment never re-downloads.
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
  2. **Sidecar override:** if `<video basename>.chapters.txt` exists next to the video, with one `H:MM:SS Title` per line, the plugin applies it and never aligns that item. It's durable, editable from the NAS, and survives Jellyfin rebuilds. The whole file must parse; any bad line rejects the sidecar into Needs review with the line number.
- **Structural validation, applied to every chapter list before `SaveChapters`** (aligned, Apply anyway or sidecar):
  - starts are finite, strictly increasing and inside `[0, runtime)`
  - titles are non-empty and at most 200 characters
  - 1–100 chapters
  - Apply anyway may bypass the confidence gates, never these checks.
  3. The NAS `phish-align.py` stays the debugging tool: it prints the per-song table without changing anything.

### 4.5 Settings

Added to `PluginConfiguration` and the settings page:
- `EnableChapterMarkers` (default **false**: the feature reads whole video files and downloads from phish.in, so other users of the public plugin opt in)
- `ChapterMarkersOnLibraryChanges` (default true; when false, only the scheduled task runs)
- `ChapterMarkersMaxShowsPerRun` (default 10) and `ChapterMarkersMaxGbPerRun` (default 100)
- `ReplaceDummyChapters` (default true)

**Credit:** the settings page credits phish.in as the audio source for chapter markers, with a link to https://phish.in (David asked for this on 2026-10-23). It sits in the chapter markers section next to the existing Phish.net attribution, and the README gets the same line.

Plus a read-only status table (counts by status, and the Needs review list with actions), served by a small `[Authorize(Policy = "RequiresElevation")]` API controller.

### 4.6 Logging and failure handling

- One Information line per show, the same shape as the fleet log: `Chapters DONE 2026-07-07 "ph2026-07-07…mkv": 18 ch, median 0.73, 0 low (read 61s, fetch 6s, align 0.5s)`. SKIP, PENDING and REVIEW lines include the reason. The per-song table goes to Debug.
- Every item runs inside try/catch. An exception sets Failed with backoff (max 5 attempts, then NeedsReview) and never stops the worker.
- ffmpeg runs with a timeout (30 min video, 2 min per MP3) and is killed on cancellation (server shutdown, task cancel). Temp files are only envelope caches and are written atomically.
- The plugin never opens a media file for writing in v1.
- **Disk contention:** `BelowNormal` CPU priority doesn't limit HDD reads. The real limits are the debounce, one show at a time, the per-run show and GB budgets, and the admin-movable task time. A show costs about one sequential file read (around 1 min on the NAS HDD), which is small next to a playback stream's needs. Playback-aware throttling is left out of v1 (see §10).

## 5. Architecture

```
ItemUpdated ──► ChapterQueue (debounce, dedup) ──┐
                                                 ├─► ChapterMarkerService.ProcessAsync(item, ct)
ChapterMarkerTask (daily / Run now, awaits) ─────┘     one show at a time (global lock + in-flight set)
                                                         ├─ ChapterStateStore   (<data>/chapters/<id>.json)
                                                         ├─ SidecarChapterReader (<name>.chapters.txt, checked first)
                                                         ├─ ChapterDetector     (missing? dummy? ours? someone else's?)
                                                         ├─ PhishInClient       (show JSON, paced MP3 streams, backoff)
                                                         ├─ EnvelopeDecoder     (ffmpeg via IMediaEncoder.EncoderPath, streaming)
                                                         ├─ TemplateCache       (<data>/phishin/*.bin, versioned header)
                                                         ├─ ConsensusAligner    (pure C#: Envelope, Ncc/Fft, TheilSen)
                                                         ├─ ChapterValidator    (structure checks on every list)
                                                         └─ IChapterManager.SaveChapters

<data> = ApplicationPaths.DataPath/phishdata (stable across plugin versions)
```

New files go under `Jellyfin.Plugin.PhishNet/Chapters/`. `ConsensusAligner`, `Ncc`, `Fft` and `EnvelopeBuilder` have **no Jellyfin dependencies**, so they're unit-testable and match the Python 1:1.

**FFT:** a self-contained iterative radix-2 complex FFT (about 80 lines), sized to the next power of two ≥ video+template frames (2^18 for a 3 h show).
- It is tested against a naive DFT and a naive NCC.
- No new NuGet dependency, so the release zip stays DLL + PDB only. MathNet.Numerics was considered and rejected because it would have to ship in the zip.
- **Precompute per show:** the video's transform and its prefix sums (Σx, Σx²) are computed once and reused by every track's global search. Only the template transform and the inverse are per track. The refine windows reuse the same NCC on a slice.
- Work buffers come from `ArrayPool`. Peak managed memory for a 3 h show should be on the order of 10 MB. Phase 5 logs the real timings and RSS on the NAS's Pentium 8505.

**Envelope while streaming:** read ffmpeg's `f32le` stdout in 400-sample hops (8000/20) and emit `log1p(sqrt(mean(x²)+1e-9)·20)` per hop. Memory is O(frames): about 0.9 MB for 3 h, not 360 MB of PCM.

## 6. Algorithm plan

**Phase 1 is a parity port**: same constants and same steps as `phish_core.align` at the version captured in the golden fixture. Acceptance: on the golden fixtures, every start time is within 0.1 s (2 frames) of Python and every NCC is within 0.01.

**Phase 2 improvements**, each judged on the fixture corpus (§7). Adopt only if no show gets worse:
- **Per-set piecewise fit:** fit the intercept per `set_name` (shared slope), falling back to the global line when a set has fewer than 2 anchors. Targets setbreak trims like 2026-07-21.
- **Order constraint in refine:** already partly there via `prev + MIN_GAP`. Add a check that the final starts are strictly increasing, and flag rather than silently reorder.
- **Encores and very short tracks** (< 90 s), where the template is the whole track: keep, and mark as low-confidence if refine < 0.30.

Bumping `algorithmVersion` makes the task re-align NeedsReview and Skipped items only. Applied items are left alone unless David triggers Retry.

## 7. Testing

The test project already has pre-existing failures, which stay out of scope. Every **new** chapter test must pass, with no exceptions hidden behind that baseline.

| Layer | Test | Data |
|---|---|---|
| `Fft`/`Ncc` | Matches naive O(n·L) NCC on random data to 1e-5; silent-window guard zeroes digital silence; `lo/hi` window bounds | Generated in-test |
| `EnvelopeBuilder` | Hop math, partial last hop dropped, known sine RMS | Generated PCM |
| `ConsensusAligner` | Synthetic shows: random "song" envelopes with gaps; drift slopes 0.95–1.08; a setbreak step; a missing track; a wrong track; all-silence; too few anchors → `few-anchors`; no agreement → `no-consensus` | Generated in-test, seeded |
| Golden parity | C# output vs Python's on real envelopes | `Fixtures/align/2026-07-07/` (video envelope + templates gz, ≈0.9 MB) made by a fixture script that imports `phish_core` |
| `PhishInClient` | Parse, soundcheck filter, 404/`audio_status`→Pending, 429 Retry-After, UA header | Recorded JSON + fake `HttpMessageHandler` |
| `EnvelopeDecoder` | Decodes a 5 s `lavfi` sine; cancellation kills the process | Skipped when no ffmpeg is on PATH |
| `ChapterDetector` | Zero chapters → missing; a single real chapter preserved; dummy signature (positive and near-miss); real chapters; our own chapters; human chapters that differ from the cache → Superseded | In-memory `ChapterInfo` lists |
| `ChapterValidator` | Unsorted, duplicate, negative, NaN and out-of-runtime starts; empty and overlong titles; bad sidecar line | In-memory |
| `ChapterStateStore` | Atomic write/read, corrupt file, schema migration, fingerprint and sidecar-hash mismatch, backoff schedule, Ignored survives an algorithm bump | Temp dir |
| `TemplateCache` | Header mismatch, non-finite samples, truncated file → re-fetch | Temp dir |
| `ChapterMarkerService` | Orchestration with fakes: applies; re-applies after a wipe; never re-aligns Applied; sidecar added or edited after Applied; file changes during decode → discarded; crash between DB save and state write; event and task on the same item at once → processed once; cancellation mid-show leaves no Applied record | Moq on `IChapterManager`, `ILibraryManager`; fake decoder and clock |
| `ChapterMarkerTask` | `ExecuteAsync` doesn't return until its items finish; progress reported; cancellation propagates; budgets stop the sweep | Fakes |

No test downloads a show or reads a real video. A local-only `Category=Corpus` test can run C# vs Python over envelopes for every show the fleet chaptered. Those envelopes are generated on the M6 from copies, kept out of git, and the test is skipped when absent.

**End-to-end, before any release:** run a throwaway Jellyfin 12 container on the M6 with a copy of 2–3 shows (one good, one 404-pending, one clip). Check that:
- chapters appear in the web player
- they survive a library scan
- they come back after "Replace all metadata", including with `DummyChapterDuration` set
- Run now stays active until the work finishes
- the Needs review page works
- chapter images behave after dummies are replaced

Never against David's live server until he chooses to install the build.

## 8. Implementation phases (TDD per phase)

0. **Environment.** Install the .NET 10 SDK on the M6 in `~/.dotnet10` (user-local, as on the M1 Pro); none is installed now. Record the current test baseline. Add `tools/make_alignment_fixture.py` and generate the 2026-07-07 golden fixture from a copy.
1. **Core port:** `Fft`, `Ncc`, `EnvelopeBuilder`, `TheilSen`, `ConsensusAligner`, with unit, synthetic and golden-parity tests.
2. **I/O adapters:** `EnvelopeDecoder` (streaming, priority, timeout, cancel), `PhishInClient`, `TemplateCache`.
3. **Jellyfin integration:**
   - `ChapterStateStore`, `ChapterDetector`, `ChapterValidator`, `ChapterMarkerService`, `ChapterQueue` (hosted service with debounce)
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

**Independent review:** Codex (`codex exec -s read-only`, 2026-10-22), asked to read this plan, the plugin code, the prior art and the Jellyfin v12.3 sources as a Jellyfin plugin architect and a media-safety reviewer. It returned 16 findings. I checked the two code-specific claims myself: the fallback path sets no `PhishNet` ID, and `DataFolderPath` is versioned. Both are true.

**Accepted and fixed in this revision:**

| # | Sev | Finding | Change |
|---|---|---|---|
| 1 | high | "DB chapters aren't ours" is unsafe; there's no owner field | Re-apply only onto zero chapters or a positively identified dummy set; otherwise **Superseded** (§4.2) |
| 2 | high | A task that only enqueues reports done early; same item can run twice | The task calls the service and awaits it; global lock plus in-flight set; one queue singleton (§4.3) |
| 3 | high | Apply anyway and sidecar can pass bad rows to `SaveChapters` | `ChapterValidator` on every list; Apply anyway bypasses confidence only (§4.4, §3) |
| 4 | high | File can change while ffmpeg reads it | Stat before and after decode and before save; 10-minute stability debounce (§4.3) |
| 5 | med | `≤ 1` chapter treated as missing would replace a real single chapter | Missing = zero, or a positive dummy match (§4.3) |
| 6 | med | Eligibility required the `PhishNet` ID, which the no-API fallback never sets | Eligibility from `PhishFileNameParser`; PremiereDate mismatch → Needs review (§4.3) |
| 7 | med | `DataFolderPath` is versioned, so an upgrade loses all state | Stable `DataPath/phishdata`, plus `schemaVersion` (§4.2) |
| 8 | med | An edited sidecar would be ignored after Applied | Sidecar checked first; its hash is part of the fingerprint (§4.3) |
| 9 | med | Dropping tracks without an MP3 shifts the timeline | Keep every non-soundcheck track; a bad duration → Needs review (§4.4) |
| 10 | med | Template cache has no versioning | Self-describing header, finite check, re-fetch on mismatch (§4.4) |
| 11 | med | Request bursts; the byte cap can silently truncate a template | 1 s pacing with jitter, per-show cap, 503 Retry-After, truncation rejected (§4.4) |
| 12 | med | CPU priority doesn't limit HDD contention | Debounce, GB-per-run budget, admin-movable trigger (default moved to 04:00) (§4.3, §4.6) |
| 13 | med | A literal port would redo the video FFT per track | Video transform and prefix sums precomputed once per show; pooled buffers (§5) |
| 14 | med | Tests miss the state and concurrency cases | Added concurrency, cancellation, crash-ordering, mutation, sidecar, cache and migration tests; new tests must be green (§7) |
| 15 | low | No `Ignored` state | Added; survives algorithm bumps (§4.3) |
| 16 | low | Thumbnail wording implied images follow automatically | Clarified, and added an E2E check (§3, §7) |

**Rejected or narrowed, and why:**
- **Use a vetted FFT package instead of a bespoke FFT** (part of #13). Narrowed: it would add a DLL to every release zip and to the manual release flow. A radix-2 FFT is small and is tested directly against a naive DFT and a naive NCC, which covers the risk. The performance half of the finding was accepted.
- **Revalidate templates with ETag or update times** (part of #10). Rejected: phish.in MP3 URLs are content-addressed blob keys, so changed audio gets a new URL. The header and finite checks guard corruption. The unused `phishInUpdatedAt` field was dropped instead.
- **Avoid overlapping playback and other media tasks** (part of #12). Deferred: one show is about one sequential file read, around a minute on the NAS HDD. Debounce, one-at-a-time and the budgets keep it bounded, and playback-aware throttling (watching `ISessionManager`) adds complexity v1 doesn't need. Revisit if the Phase 5 timings on the NAS say otherwise.
