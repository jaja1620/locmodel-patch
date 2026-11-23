# LocModel Network Fix Toolkit

[简体中文](README.md) | **English**

> LocModel cannot reach `huggingface.co` from mainland China. Symptoms: the model list stays empty and downloads fail outright. This repository ships two complementary solutions: **v1.0 mirror patch** (flip one config field so LocModel itself uses a mirror) and **v2.4.2 standalone downloader** (full GUI downloader that never touches LocModel, with multi-site speed probing, automatic best-line selection, a LocModel **3.5 / 3.3 / 3.1** version switch, an in-app **online search that can download models from any Hugging Face repo**, concurrent segmented downloading for speed in **v3.0.0**, a **pre-download re-check that highlights already-downloaded files** in **v3.1.0**, plus LocModel 3.6 support, and, in the latest **v3.1.1**, no more implicit `huggingface.co` fallback (the “download sites” box is the only source of endpoints) and a **LocModel-scan-first model list with the built-in snapshot as fallback**).

---

## Table of contents

- [1. Version matrix](#1-version-matrix)
- [2. The problem](#2-the-problem)
- [3. v1.0 design: the patch approach](#3-v10-design-the-patch-approach)
- [4. v2.2 / v2.3 design: the standalone-downloader approach](#4-v22-design-the-standalone-downloader-approach)
- [5. Design differences between v2.2 and v1.0, and why](#5-design-differences-between-v22-and-v10-and-why)
- [6. v2.2 usage (detailed)](#6-v22-usage-detailed)
- [7. v1.0 usage (still available)](#7-v10-usage-still-available)
- [8. Compatibility and tested scope (must read)](#8-compatibility-and-tested-scope-must-read)
- [9. Verification log](#9-verification-log)
- [10. Build it yourself](#10-build-it-yourself)
- [11. Version retention policy](#11-version-retention-policy)
- [12. Disclaimer and license](#12-disclaimer-and-license)

---

## 1. Version matrix

| Version | Form | What it does | LocModel must be running? | Status |
| --- | --- | --- | --- | --- |
| **v1.0.0** | Single-file console patch `locmodel_patch.exe` (18 KB) | Rewrites `custom_mirror` in `settings.json` to `https://hf-mirror.com` so LocModel's own download page goes through the mirror | Tries a graceful close before editing, can restart afterwards | Verified on LocModel 2.1.0 ✅ / 3.1.0 ✅ |
| **v2.2.0** | Single-file GUI downloader `LocModel下载器_v2.2.exe` (88 KB) | Standalone downloader covering all 28 LocModel models; custom download sites (multiple) with automatic speed probing and best-line selection | Not required; it only reads/writes the same model directory | Verified on LocModel 3.1.0 ✅ (**not tested on 2.1.0**) |
| **v2.2.1** | Single-file GUI downloader `LocModel下载器_v2.2.1.exe` (89 KB) | Bug-fix release for v2.2.0: fixes the endless `校验失败(数组维度超过了支持的范围。)` loop on model files larger than 2 GiB, which deleted the finished `.part` and restarted from zero | Same as v2.2.0 | Verified on LocModel 3.1.0 ✅ (**not tested on 2.1.0**) |
| **v2.3.0** | Single-file GUI downloader `LocModel下载器_v2.3.exe` (101 KB) | Capability extension of v2.2.1: adds a **LocModel version selector** (3.3 by default, switchable back to 3.1), covers all 29 models of 3.3 (13 image/video + 16 text/reasoning), and adapts to the new 3.3 settings such as `accel_lines` and the LoRA directory. **The download / verification / resume core is byte-for-byte identical to v2.2.1** | Same as v2.2.0 | Verified on LocModel 3.3.0 ✅ / 3.1.0 ✅ |
| **v2.4.0 / v2.4.1** | Single-file GUI downloader (166 KB) | Adds **online model search & download** (search any Hugging Face repo, pick files, then download through the same local kernel: custom sites, speed probing, resume and verification all reused) plus **GGUF quantization conversion** (fetch source weights, convert locally). **Kernel byte-identical to v2.3** | Same as v2.2.0 | Verified on LocModel 3.3.0 ✅ / 3.1.0 ✅ (sources and baseline exe kept in `v2.4.1/`) |
| **v2.4.2** | Single-file GUI downloader `LocModel下载器_v2.4.2.exe` (176 KB) | Version-adaptation release of v2.4.1: adds **LocModel 3.5 support (default profile)** while 3.3 / 3.1 stay selectable; adapts to the 19-field 3.5 catalog, Unsloth Dynamic quants and new settings (`hf_token`, `accel_nodes`, ...); detail panel gains a “Model spec: 180B (active 6B) · context 256K · 48 layers” line; detects in-place `.part` files left by 3.5 (report only). **Kernel byte-for-byte identical to v2.2.1 / v2.3 / v2.4.1** | Same as v2.2.0 | Verified on LocModel 3.5.0 ✅ / 3.3.0 ✅ / 3.1.0 ✅ (catalog self-check) |
| **v3.0.0** | Single-file GUI downloader `LocModel下载器_v3.0.0.exe` (195 KB) | Download-acceleration release of v2.4.2: adds **concurrent segmented download** (follows 302/307 manually while re-sending Range, throttles concurrency by the measured sequential write speed of the target disk, flushes segment progress every 2 s), plus a connection-count selector (1–16, default 4) and a segment status label; default site list trimmed to `hf-mirror.com`. Falls back to the single-connection kernel when segmentation does not apply | Same as v2.4.2 | Verified on a real 942 MB file: 4 connections, 13.9 s, 67.77 MB/s, SHA-256 matched; **tag only — no GitHub Release created** |

| **v3.1.0** | Single-file GUI downloader `LocModel下载器_v3.1.0.exe` (201 KB) | Usability release on top of v3.0.0: adds a **pre-download re-check**. Every entry in the "Files to download" panel is verified against the local disk (target exists and is non-empty, no `.part` / `.part.meta` leftovers next to it, and length not below the declared size when known); matching rows are rendered **green** with **（已存在）** appended, and the panel title reports "N total, M already present". Also adds a **LocModel 3.6 profile (default)**, with 3.5 / 3.3 / 3.1 still selectable. The re-check is strictly read-only and the **download kernel is byte-identical to v3.0.0** | Same as v3.0.0 | Verified: `--selftest` 29/29, sampling of a real model directory (z-image-turbo 4 items / 3 green, qwen-image-2.1 5 items / 3 green, a main file with `.part` residue correctly reported as not downloaded), pixel-level check of the owner-drawn green rows, and a local 24 MB / 4-connection segmented download regression |
| **v3.1.1** | Single-file GUI downloader `LocModel下载器_v3.1.1.exe` (211 KB) | Two behaviour fixes on top of v3.1.0: **① no implicit `huggingface.co` fallback** — the “download sites” box is now the only source of endpoints (the built-in table keeps only `hf-mirror.com`, speed ranking no longer appends built-in lines, online search and link mapping default to the same site value, and any site not typed into the box never enters the download path; if every listed site fails, the error is reported as-is); **② the model list is now “LocModel scan first, built-in snapshot as fallback”** — the language/reasoning list resolves in three tiers (scan → server `catalog.json` → built-in snapshot), while image/video entries take the scan as authoritative once ≥ 6 entries parse and the snapshot is only ever topped up, never trimmed (quant levels are unioned, dependencies only filled when the snapshot has none), so models added in minor LocModel updates show up (e.g. `wan2.1-i2v-14b` in this build). The site policy change touches `Downloader.cs`, but only the site table and the empty-fallback branch; segmentation, resume and verification logic are untouched and `DownloadSeg.cs` stays byte-identical to v3.1.0 | Same as v3.0.0 | Verified: `--selftest` 30/30 (including 3 new site-chain assertions and 2 catalog-source assertions), a single-site real download (12 MiB through the whole kernel, matching MD5, zero hits for `hf-mirror.com` / `huggingface.co` in the log), a 404 case that only retried the configured site without switching, and a 14 + 16 entry catalog check |
The two versions do not conflict:

- Just want LocModel itself to download again → use the v1.0 patch, done in 30 seconds.
- Want multiple mirrors, speed probing, controlled directory, robust resume → use the v2.2 downloader.
- Using both is fine: v1.0 only changes one field in `settings.json`; v2.2 never touches LocModel.

All historical artifacts stay in the repository and in Releases; nothing is deleted (see [section 11](#11-version-retention-policy)).

---

## 2. The problem

LocModel hardcodes the official HuggingFace domain in its download path:

- `https://huggingface.co` is reset / DNS-poisoned in mainland China (on the test machine it resolved to `199.59.148.8`, curl timed out after 12 s, exit code 000).
- Result: empty model list, "network unavailable", download errors, broken transfers halfway.
- `https://hf-mirror.com` is a usable domestic mirror: model list, repo info and Range requests all work (HTTP 200 / 206 in tests), and once connected, downloads run at roughly 25–90 MB/s.

Both product lines solve the same thing — **replace "must connect to huggingface.co" with "go through a working mirror"**. They differ only in which layer they change.

---

## 3. v1.0 design: the patch approach

**Core judgement: LocModel already exposes a mirror entry point (`custom_mirror`), so change only that field and leave the binary alone.**

What it does:

1. Locate `settings.json`: next to the exe → `%LOCALAPPDATA%\LocModel\settings.json` → registry `InstallLocation` directory.
2. If LocModel is running, **attempt a graceful close** (wait up to 15 s, then abort — never force-kill).
3. Back up the file as `settings.json.bak.yyyyMMdd-HHmmss`.
4. **Change only the value of `custom_mirror`**; indentation, line endings, BOM, key order and trailing newline are preserved byte for byte.
5. Re-read and verify; on mismatch, roll back automatically from the backup.
6. Restart LocModel if it was running.

Strengths: tiny (18 KB), zero dependencies, reversible (backup + `--dry-run`), no knowledge of model directories, no interference with download logic.

Cost: **the ceiling is set by LocModel itself.** Only one mirror; no speed probing; no multi-line preference; download behaviour is subject to LocModel's own retry policy and its membership fast-line mechanism (since 3.1.0, `hf_mirror` plus member-only lines); after a failed download LocModel rewrites `custom_mirror` back to the member line, so you must restart the app and try again.

---

## 4. v2.2 / v2.3 design: the standalone-downloader approach

**Core judgement: LocModel exposes no programmable entry point and its download logic is sealed inside the exe, so bypass it entirely — write a download-only GUI that writes into the same model directory.**

Evidence that ruled out the port-injection idea:

- The LocModel main process (Tauri + WebView2) **listens on no TCP/UDP port**: zero hits for `axum` / `warp` / `actix` / `TcpListener` / `HttpServer` / `tiny_http`, zero hits for `CorsLayer`.
- All capabilities are Tauri commands (`get_settings`, `save_settings`, `download_model`, `set_hf_mirror`, `accel_plan`, … 30+), invoked via `window.__TAURI_INTERNALS__.invoke` → WebView2 `postMessage`, origin `tauri://localhost`; external pages (`file://`, `http://`) cannot call them.
- The `port=8686` in settings is only the llama.cpp engine port (not listening when idle); `8790` is the stable-diffusion.cpp WebUI with just `/v1/models`, `/sdapi/v1/*` routes — neither has download/settings semantics.

Conclusion: **port injection is not viable**, hence the "standalone downloader aligned with LocModel's on-disk layout" route.

What v2.2 does:

1. On startup it reads LocModel's `settings.json` for the real model directory (with drive-letter drift checks) and parses all 28 models (6 image + 6 video + 16 language/reasoning), every quant level, multi-file dependencies and the shared `image\common` components.
2. On-disk layout is identical to LocModel, so downloaded files are recognized immediately:
   - Image / video → `{dir}\image\{model-id}\{upstream-filename}`
   - Shared components → `{dir}\image\common\{filename}`
   - Language / reasoning → `{dir}\{org}__{repo}\{filename}`
3. **Custom download sites (new in v2.2)**: a site input bar at the top, pre-filled with `https://hf-mirror.com`, accepting multiple mirrors. Before each download it probes every site (3 in parallel, 256 KB real Range read per site, 8 s timeout), ranks them by measured speed and **downloads over the fastest line**, falling back to the next on failure.
4. Download hardening: HEAD pre-check for version changes, `.part` + `.part.meta` resume, 416 self-healing, isolated temp directory, verification ladder (length → HTML error page → GGUF magic / safetensors header → SHA-256), and success-line promotion.
5. It never modifies LocModel: no patching, no injection, no registry changes — only model files and its own config.

### 4.1 Changes in v2.3: LocModel version selector (3.3 / 3.1)

LocModel 3.3.0 changed both the model catalog and the config layout (new `minimax-h3-image`; `settings.json` gained `accel_lines`, `account_base`, `comfy_dir`, `images_dir`, `imported`, `perf_*`; `hf_mirror` turned from a URL into the enum value `member`; engine config moved from `engines.json` to `engines\engine.json`). v2.3 adds **a version adaptation layer instead of touching the core**:

| Change | 3.1 | 3.3 | How v2.3 handles it |
| --- | --- | --- | --- |
| Model count | 28 (6 image + 6 video + 16 text) | 29 (13 image/video + 16 text) | The catalog is reloaded when the selector changes; 3.3 adds `minimax-h3-image` |
| Version detection | — | — | File version of `LocModel.exe` plus `accel_lines` in the config; the detected result is shown at the top of the window |
| `hf_mirror` | URL string | Enum `member` / `custom` | Under 3.3 it is no longer treated as a mirror URL; `custom_mirror` and `accel_lines` are used instead |
| Mirror lines | 4 built-in | 4 built-in + `accel_lines` (membership lines) | `accel_lines` is injected into the catalog probe list; the download line table is unchanged |
| LoRA | none | new `{dir}\image\loras` | Recognized and displayed only, **never managed for download** (3.3 exposes LoRA through its own `/sdapi/v1/loras`) |
| `comfy_dir` | none | ComfyUI directory (may be empty) | Read-only detection with a hint when empty; the user config is **never rewritten** |
| Temp directory | none | `{dir}\_locmodel_dl_tmp` (used by 3.3 itself) | The downloader uses `{dir}\_locmodel_dl_tmp\lmv23` so it never collides with the host app |

**Zero-change boundary for the core**: `Downloader.cs` (HEAD pre-check, `.part` + `.part.meta` resume, 416 self-healing, mirror rotation, stall detection, SHA-256 verification, the >2 GiB verification fix) and `SiteProbe.cs` (site parsing and speed probing) are **byte-for-byte identical** across the two versions. v2.3 only adds the `LocModelProfile.cs` layer around them and adjusts the UI and catalog source.

A new row at the top of the window:

```
LocModel version: [ 3.3 (default) v ]   Detected: D:\...\LocModel.exe · 3.3.0    (reloads catalog and directory rules on change)
```

The selection is stored by the downloader itself in `LocModel下载器_版本配置.txt` next to the exe — **never in LocModel's `settings.json`** — so switching versions cannot affect the host app.

---

### 4.2 Changes in v2.4.2: online search download + LocModel 3.5 support (3.5 / 3.3 / 3.1)

**(1) What v2.3 did not have: download models found by custom search.** The built-in catalog only lists LocModel's own 29 models. v2.4.x adds an in-app **online search**: type a keyword, search Hugging Face repos, pick a repo, list its files (`.gguf` quants and `mmproj` vision projectors included) and download them through the **same local kernel** — custom sites, speed probing, resume, SHA-256 verification and mirror rotation all apply to any repo. New files: `HubBrowserForm.cs` / `HubSearch.cs` / `HubUrlMapper.cs`, plus GGUF quantization conversion (`ConvertService.cs` + `RepoQuants.cs`).

**(2) v2.4.2's version adaptation: LocModel 3.5.**

| Change | 3.3 | 3.5 | How v2.4.2 handles it |
| --- | --- | --- | --- |
| Catalog fields | 10 | **19** (adds `params_b` / `active_b` / `layers` / `kv_layers` / `kv_heads` / `head_dim` / `experts` / `experts_used` / `ctx_max`) | Parser covers all 19, degrades gracefully on gaps; specs feed both the detail panel and internal validation |
| Quant naming | `Q2_K … Q8_0` | **Unsloth Dynamic**: `UD-Q2_K_XL … UD-Q8_K_XL` (`F16` for `gpt-oss`) | No more hard-coded quant names; files are resolved from the actual repo listing |
| `settings.json` | 20+ fields | **36 fields**, adding `hf_token` / `github_mirror` / `accel_nodes` / `preferred_line` / `preferred_ip` | Read-only; `hf_token` is sent only when connecting directly to `huggingface.co`, never to `hf-mirror.com`, and is masked in logs |
| Partial downloads | `_locmodel_dl_tmp\lmv23\*.part` | in-place `<file>.part` + `.part.meta` | New `HalfPartScan.cs` reports "unfinished download N MB" — **report only, no takeover, no deletion, no resume** |
| Image/video catalog | 13 entries | 13 entries (unchanged) | Embedded snapshot is authoritative; exe parsing is used only for existence checks, avoiding the old parser's 2 missing entries and misattributed VAE repos |
| On-disk layout | `{dir}\{org}__{repo}`, `{dir}\image\{slug}`, `{dir}\image\common` | unchanged | unchanged; slug mapping pinned (`flux.1-schnell → flux1-schnell`, ...) to match LocModel's own folders |

**Kernel-unchanged boundary**: `Downloader.cs` (SHA-256 prefix `313DC3B74651`) and `SiteProbe.cs` (`1915B5167DD4`) are byte-for-byte identical across v2.2.1 / v2.3 / v2.4.1 / v2.4.2, and `HubBrowserForm.cs` / `HubSearch.cs` / `HubUrlMapper.cs` / `RepoQuants.cs` are untouched from v2.4.1 to v2.4.2. Changes are confined to `LocModelProfile.cs` (version diff table), `CatalogService.cs` (catalog parsing / slugs / snapshot authority), `Models.cs` (spec fields), `MainForm.cs` (UI) and the new `HalfPartScan.cs`.

The header now reads:

```
LocModel 版本：[ 3.5（默认） v ]   探测：D:\...\LocModel.exe · 3.5.0        (switching reloads the catalog and folder rules)
```

---

## 5. Design differences between v2.2 and v1.0, and why

| Dimension | v1.0 patch | v2.2 downloader | Why it changed |
| --- | --- | --- | --- |
| What it changes | One field in LocModel's `settings.json` | Nothing in LocModel; a separate process writes the model directory | LocModel's GUI has no listening port and only speaks Tauri IPC, so the only way to add capability is to implement downloading yourself |
| Number of mirrors | Exactly one (`custom_mirror` is a single value) | Unlimited, one per line, auto-normalized and de-duplicated | A single mirror leaves you stuck when it degrades; multiple sites require the program itself to choose |
| Line selection | Internal to LocModel, user has no say | Automatic speed probing before download, fastest wins, fallback on failure | The user asked for "fill in several sites and let it pick the fastest" — LocModel cannot do that |
| Coverage | Only LocModel's own download page | All 28 models, including dependency files that page does not list | The built-in page hides details like `image\common`; the standalone tool fetches everything in one go |
| Resume control | Constrained by LocModel; after an error `custom_mirror` is rewritten back to the member line and the app must be restarted | Own `.part` resume + HEAD pre-check + ETag change detection + 416 self-healing | Since 3.1.0 introduced member fast lines, LocModel rewrites config, making it hard to stay on the mirror line |
| Directory control | Not involved | Custom model directory, red warning + confirmation when it differs from the default | Swapping external drives changes drive letters and LocModel's config drifts (`G:` → `F:` on the test machine was a real case) |
| Risk surface | Modifies config (backup, rollback, `--dry-run`) | Writes model files only, changes no config | Different risk profiles: the patch must be careful with config, the downloader with files |
| Size | 18 KB console | 88 KB GUI | The requirement grew from "fix one line" to "multi-site probing + a full downloader" |
| Dependencies | .NET Framework 4.x (bundled with Windows) | Same, single file | Keeps "no runtime install needed on the target machine" |

In one sentence: **v1.0 makes LocModel walk the mirror; v2.2 becomes the downloader itself.** The former works in 30 seconds but is capped by LocModel; the latter is more work but puts mirror choice, line selection, resume and directory control in your hands. That is why v2.2 keeps every v1.0 finding (hf-mirror works, huggingface.co does not) while upgrading "a mirror" from a config value into "a list of sites you can benchmark".

---

## 6. v2.2 usage (detailed)

### 6.1 Get and run

1. Download `LocModel-Downloader-v2.2.exe` from [Releases](https://github.com/jaja1620/locmodel-patch/releases) (the release asset uses an ASCII name to avoid mojibake; the in-repo binary is `v2.2/LocModel下载器_v2.2.exe`).
2. Double-click. No installation, no runtime install (Windows 10/11 ships .NET Framework 4.8).
3. Keep it in a stable folder (e.g. `D:\Tools\LocModelDownloader\`) — the site config file lives next to the exe.

### 6.2 UI overview

| Area | Contents |
| --- | --- |
| Top bar | "Reload from LocModel", model directory box + "Browse…", "Start download" / "Cancel", version v2.2 |
| Site bar (new in v2.2) | "Custom download sites" multi-line box, "Test speed", "Restore default", status line showing per-site speed |
| Left | Category (All / Image / Video / Language·Reasoning) + model list, 28 entries |
| Right | Title, repo, description, quant selector, "Files to download" list, progress bar, log |

### 6.3 Custom sites and automatic speed selection (the v2.2 core)

1. **Default**: the box is pre-filled with `https://hf-mirror.com` on first launch; you can download without editing anything.
2. **Multiple sites**: one per line, or separated by commas / semicolons / spaces:

   ```
   https://hf-mirror.com
   https://my-mirror.example.com
   https://another.example.net/hf
   ```

3. **Auto-normalization**: missing scheme gets `https://`, trailing slashes are trimmed, scheme is lower-cased, duplicates removed (same scheme+host+port = same site), invalid entries dropped with a hint.
4. **Test speed**: sends `Range: bytes=0-262143`, actually reads 256 KB, computes bytes ÷ elapsed. 3 sites in parallel, 8 s timeout per site. The status line shows e.g. `Probe: hf-mirror.com 3.21 MB/s | mirrorB 260 KB/s | mirrorC unavailable`, and the log prints the resulting line order.
5. **Automatic best line on download**: each "Start download" first probes using the **largest file of the batch** (so the probe matches the real payload), then ranks by measured speed and **downloads over the first one**; on failure it falls back to the next; winning lines are promoted, failing lines demoted.
6. **Ranking rule**: usable sites by speed → responded but delivered no data → connection failure/timeout/HTTP error (last, still kept as a fallback).
7. **Restore default**: resets the bar to a single `https://hf-mirror.com`.
8. **Persistence**: sites are written to `LocModel下载器_站点配置.txt` next to the exe (one per line) and reloaded on next start; you may edit that file directly.
9. **`*.locmodel.com` is excluded**: `m1` / `m2` / `activation` / `get.locmodel.com` are LocModel's own CDN and do not proxy HuggingFace paths (verified 404 on `/{repo}/resolve/main/...`). Even if you paste them into the site bar they are filtered out, so you never waste a timeout on them.

### 6.4 Download flow

1. On startup it reads model directory, port, mirror settings and all model parameters.
2. Pick a category on the left → pick a model → pick a quant level (e.g. `Q4_K_M`, `BF16`).
3. Review "Files to download": main file, `mmproj`, `image\common` dependencies, with sizes.
4. Click "Start download": it probes first (progress shows "Probing N sites…"), then downloads over the fastest line with total bytes, live speed and percentage.
5. "Cancel" keeps the `.part`; the next start resumes automatically.
6. Files that already exist and pass verification are skipped; shorter files are converted into a resume instead of being re-downloaded.
7. On completion a dialog shows the target folder; refresh the model list in LocModel to see them.

### 6.5 Where files go

- Default = the model directory from LocModel's `settings.json` (on the test machine `F:\AI-Models`, while the config still said `G:`, a drive that no longer exists; the downloader trusts `settings.json` and displays it faithfully).
- You can type or browse to a custom directory.
- If it differs from LocModel's default, the top bar shows a red warning and a confirmation dialog appears before downloading:
  "The custom directory differs from LocModel's default directory. Either change LocModel's default directory, or copy the downloaded content into the default directory before using it."

### 6.6 FAQ

| Symptom | Cause and fix |
| --- | --- |
| A few seconds of waiting after clicking Start | Speed probing: 8 s per site, 3 in parallel, so up to ~8 s with many sites. Expected. |
| A site shows "unavailable" | It did not return 200/206 for the HF path, or refused the connection. It is pushed to the back and does not affect others. |
| Downloads are slow | Check the status line for the fastest site; keeping only the fastest one or two sites also shortens probing. |
| Can I use my own reverse proxy? | It must serve `/{repo}/resolve/main/{path}`. Net-disk share links and HTML download pages will not work. |
| LocModel does not see the downloaded model | Make the download directory match LocModel's default; for a custom directory, follow the red warning. |
| The model drive letter changed | After swapping external drives LocModel's config does not follow. Fix it in LocModel's settings, or select the right directory here. |

---

## 7. v1.0 usage (still available)

v1.0 remains fully functional; its files and Release assets are untouched. Full manual: [README-v1.0.0.md](README-v1.0.0.md) (Chinese). Quick reference:

```
locmodel_patch.exe                 # locate and fix automatically
locmodel_patch.exe --dry-run       # preview changes, write nothing
locmodel_patch.exe --path "..."    # explicit settings.json or its folder
```

Exit codes: `0` success (including "already the target value") | `1` unknown error / bad arguments | `2` config not found | `3` no `custom_mirror` key | `4` write or verification failed (rolled back) | `5` could not close LocModel.

Note: since 3.1.0, a failed download makes LocModel rewrite `custom_mirror` back to the member line. Do **not** press Retry — quit and relaunch the app, then start the download again.

---

## 8. Compatibility and tested scope (must read)

| Item | Result |
| --- | --- |
| **v2.2.0 downloader** | **Tested only on LocModel 3.1.0; NOT tested on LocModel 2.1.0.** Whether 2.1.0 shares the same directory layout and config fields was not verified — evaluate before use |
| **v2.3.0 downloader** | **Verified on LocModel 3.3.0 ✅** locally (29 models; the 13 image/video entries match the catalog embedded in the exe one by one); switching to the 3.1 profile reproduces the 28-model catalog of v2.2.1 |
| **v1.0.0 patch** | Verified on both LocModel 2.1.0 ✅ and 3.1.0 ✅ |
| OS | Windows 10 / 11 (.NET Framework 4.8, bundled) |
| Network | `hf-mirror.com` reachable; `huggingface.co` DNS-restricted on the test machine, used only as a last fallback |

> v2.2 relies on the config path and fields measured on 3.1.0 (`%LOCALAPPDATA%\LocModel\settings.json`). If you run it on 2.1.0 and the directory detection looks wrong, please open an issue with your version and the on-screen message.

---

## 9. Verification log

**v2.2 site features (27 cases, all PASS)**

| Group | Coverage | Result |
| --- | --- | --- |
| A Parsing / normalization / chain merge | 15 cases (mixed separators, scheme fill-in, trailing slash, dedupe, own-CDN exclusion, HF chain order, config round-trip, …) | 15/15 |
| B Speed ranking | 5 cases (dead port last, fastest first 3.21 MB/s / 78 ms, slow second 260 KB/s / 984 ms, >3× spread) | 5/5 |
| C Best-line download | 5 cases (31,457,280 bytes verified, request only hits the best site, slow site unused, automatic fallback when the best one dies) | 5/5 |
| D Real hf-mirror.com probing | 2 cases (161 KB/s / 1593 ms, finished within 10 s) | 2/2 |

**v2.2 download pipeline (inherited from v2.1, all PASS)**: 21 local Range scenarios (fresh, resume, ETag/Last-Modified change, oversized/equal-length part, no-Range server, cancel-and-resume, partial destination adoption, unknown expected size, verification retry cap, per-file failure not aborting the batch, HEAD-level version interception, disk-space pre-check, SHA match/mismatch, cross-mirror ETag decision) plus 4 real-network scenarios (fresh download, correct-ETag resume, forged-ETag version change, oversized part discarded).

**v1.0 measurements (historical)**: passed on LocModel 2.1.0 and 3.1.0; packet capture on 3.1.0 confirmed downloads resolve through `hf-mirror.com` to the xet-bridge CDN without a direct `huggingface.co` connection; a 5690 MB model downloaded at 448–567 KB/s with working `.part` resume; mirror-line throughput 25–90 MB/s.

---

## 10. Build it yourself

**v2.2 downloader** (compiler bundled with Windows, no Visual Studio):

```bat
c:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001 /target:winexe ^
  /out:LocModel下载器_v2.2.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Core.dll ^
  v2.2\src\Downloader.cs v2.2\src\CatalogService.cs v2.2\src\Models.cs v2.2\src\MainForm.cs v2.2\src\SiteProbe.cs
```

**v2.3 downloader** (one extra file, `LocModelProfile.cs`):

```bat
c:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001 /target:winexe ^
  /out:LocModel下载器_v2.3.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Core.dll ^
  v2.3\src\Downloader.cs v2.3\src\CatalogService.cs v2.3\src\Models.cs v2.3\src\LocModelProfile.cs v2.3\src\MainForm.cs v2.3\src\SiteProbe.cs
```

**v3.0.0 downloader** (adds `DownloadSeg.cs` and `DownloadConfig.cs`, two extra compression assemblies):

```bat
c:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001 /target:winexe ^
  /out:LocModel下载器_v3.0.0.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Core.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  v3.0.0\src\*.cs
```

**v3.1.0 downloader** (adds `DepsPrecheck.cs`; same command as v3.0.0, only the directory changes):

```bat
c:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001 /target:winexe ^
  /out:LocModel下载器_v3.1.0.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Core.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  v3.1.0\src\*.cs
```

**v3.1.1 downloader** (adds `TextCatalogSnapshot.cs`; same command as v3.1.0, only the directory changes):

```bat
c:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001 /target:winexe ^
  /out:LocModel下载器_v3.1.1.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Core.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  v3.1.1\src\*.cs
```

**v1.0 patch**:

```bat
c:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:exe /codepage:65001 /optimize+ /out:locmodel_patch.exe locmodel_patch.cs
```

---

## 11. Version retention policy

- All v1.0 files (`locmodel_patch.cs`, `locmodel_patch.exe`, `build.cmd`, original `README-v1.0.0.md`) stay in the repo unchanged.
- Release `v1.0.0` **is kept**, new versions are published as additional tags.
- v2.2 lives in `v2.2/` and Release `v2.2.0`, overwriting nothing.
- v3.0.0 lives in `v3.0.0/` plus tag `v3.0.0`; that release is **tagged only, no GitHub Release was created** — the binary and source zip sit in the repo root (`LocModel-Downloader-v3.0.0.exe`, `locmodel-downloader-v3.0.0-src.zip`) and in `v3.0.0/`.

- v3.1.0 lives in `v3.1.0/` plus tag `v3.1.0` (`LocModel-Downloader-v3.1.0.exe`, `locmodel-downloader-v3.1.0-src.zip`, `RELEASE_NOTES_v3.1.0.md`).
- v3.1.1 lives in `v3.1.1/` plus tag `v3.1.1`, and ships a GitHub Release marked as **Latest** (`LocModel-Downloader-v3.1.1.exe`, `locmodel-downloader-v3.1.1-src.zip`, `RELEASE_NOTES_v3.1.1.md`); the tags, directories and Release assets of v3.1.0 and earlier are never deleted or overwritten — v3.1.0 merely loses the Latest label.
| Release | Assets |
| --- | --- |
| `v1.0.0` | `locmodel_patch.exe`, `locmodel_patch.cs`, `README.md` (original) |
| `v2.2.0` | `LocModel-Downloader-v2.2.exe`, Chinese manual `README_zh-CN.md`, English README `README_EN.md`, source `locmodel-downloader-v2.2-src.zip` |
| `v2.2.1` | `LocModel-Downloader-v2.2.1.exe`, source `locmodel-downloader-v2.2.1-src.zip`, notes `RELEASE_NOTES_v2.2.1.md` |
| `v2.3.0` | `LocModel-Downloader-v2.3.exe`, source `locmodel-downloader-v2.3-src.zip`, notes `RELEASE_NOTES_v2.3.md` |
| `v3.1.0` | `LocModel-Downloader-v3.1.0.exe`, source `locmodel-downloader-v3.1.0-src.zip`, notes `RELEASE_NOTES_v3.1.0.md` |
| `v3.1.1` | `LocModel-Downloader-v3.1.1.exe`, source `locmodel-downloader-v3.1.1-src.zip`, notes `RELEASE_NOTES_v3.1.1.md` |

---

## 12. Disclaimer and license

- This project is not affiliated with LocModel. It does not modify the LocModel binary and does not bypass any licensing or membership mechanism.
- v1.0 edits `settings.json` (automatic backup, rollback, plus `--dry-run`); v2.2 only writes model files and its own config. Use them with that in mind.
- Model files remain under the license of their upstream HuggingFace repositories.
- License: see [LICENSE](LICENSE).

*（This document was generated with AI assistance.）*
