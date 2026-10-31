# DREX PC CLEANER v2

Free, lightweight Windows cleaner + privacy + Windows Defender dashboard. C# / .NET 10 / Windows Forms, no third-party dependencies, no services, no scheduled tasks, no background scanning.

## Features
- **Dashboard** – safe TEMP scan, Auto Clean on launch, memory ring and system-drive bar (refresh every 5 s, paused when minimized).
- **Security** – reads Windows Defender status, runs Quick/Full scan, updates definitions, checks Firewall / UAC / Remote Desktop and shows a score. DREX has *no* antivirus engine of its own; it drives the one built into Windows.
- **Deep Clean** – thumbnails, crash dumps, error reports, Chrome/Edge/Brave/Firefox caches (never passwords, cookies or sessions), recent-files list, Recycle Bin. Analyze first, confirm before deleting.
- **Startup** – list entries with publisher; reversible Enable/Disable (same flag Task Manager uses).
- **Threats / Event Log** – Defender detection history and operational log. **Scheduler** – optional weekly scan and cleanup (Task Scheduler). **File Vault** – AES-256-GCM encrypt/decrypt and secure delete. USB drives are detected when connected.
- **Scanner / Quarantine** – DREX's light scanner (EICAR + a hash list you import) with quarantine. Complements Defender.
- **Large Files**, **Duplicates** (content hash, deletes go to the Recycle Bin), **Disk Usage** (drill-down), **Web Blocker** (hosts file), **Backup** (to ZIP), **History**, **Settings**.

## Build
Push to GitHub; Actions builds `DREX_PC_CLEANER.exe` (self-contained). Or run `build.bat` with the .NET 10 SDK.

## Notes / limitations
- Windows TEMP, Web Blocker and Defender updates need *Restart as administrator* (Settings).
- Locked files are skipped and counted, never forced.
- Not yet implemented: file encryption and secure-delete (needs extra testing; secure delete is unreliable on SSDs).
- Performance numbers must be measured on your PC (Task Manager).
