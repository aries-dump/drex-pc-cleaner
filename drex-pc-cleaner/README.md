# DREX PC CLEANER

Lightweight Windows cleaner (C# / .NET 10 / Windows Forms, no third-party dependencies, no services, no scheduled tasks).

## Build (Windows)
1. Install the .NET 10 SDK.
2. Run `build.bat`. Output:
   - `dist\framework-dependent\DREX_PC_CLEANER.exe` – small, requires .NET 10 Desktop Runtime.
   - `dist\self-contained\DREX_PC_CLEANER.exe` – runs anywhere, larger.

## Features
- **Launch flow:** safe TEMP scan → real reclaimable size → optional Auto Clean (ON by default) → report. History in `%APPDATA%\DrexPcCleaner\history.log` (rotated at 128 KB).
- **Safe clean:** User TEMP and Windows TEMP, files older than N hours (default 1), skips locked files, symlinks/junctions.
- **Deep Clean:** thumbnail/icon cache, crash dumps, WER reports, Recycle Bin. Always analyze first; confirmation required (configurable).
- **Startup:** reads Run keys and Startup folders (read-only), shows publisher, flags updater/helper entries and missing targets, opens Windows Startup Apps settings. Nothing is disabled automatically.
- **Large Files:** folder picker, threshold (default 500 MB), cancellable, sort by size, open location. Never deletes.
- **RAM monitor:** `GlobalMemoryStatusEx`, 5-second timer, paused when minimized or disabled. No processes are killed.
- **Settings:** Auto Clean, min age, Deep Clean confirmation, RAM monitoring, launch with Windows (OFF by default, HKCU Run value).

## Performance design
Streaming `EnumerateFiles`, one directory category scanned at a time, no result caching, non-concurrent GC, pages built lazily on first visit, no timers while idle other than the optional 5 s RAM tick.

## Status / known limitations
- **This source has NOT been compiled or benchmarked.** It was written without access to a Windows machine or .NET SDK. Expect to fix minor compile errors on first build.
- Performance targets (RAM, CPU, startup, EXE size) are goals; measure with Task Manager / `Get-Process` and fill in the table below.
- Windows TEMP and system crash dumps need Administrator rights; inaccessible files are skipped and counted.
- Thumbnail cache files may be locked by Explorer and skipped.
- Startup scan does not read Task Scheduler or services (by design).

## Benchmark report (to be filled after running on Windows)
| Metric | Result |
|---|---|
| Idle RAM | not measured |
| Peak RAM during scan | not measured |
| Idle CPU | not measured |
| Startup time | not measured |
| EXE size (both builds) | not measured |

## Build without installing the SDK (GitHub Actions)
1. Create a free GitHub repo and upload this whole folder (including `.github`).
2. Open the **Actions** tab → "Build DREX PC CLEANER" → **Run workflow**.
3. When it finishes, download the `DREX_PC_CLEANER` artifact (zip). The `self-contained` EXE runs on any Windows 10/11 PC with nothing installed.
