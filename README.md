# OptiScaler Tools

A portable Windows app for preparing OptiScaler nightlies, installing them into a selected game, and editing OptiScaler.ini. Open **OptiScaler-Tools.exe** to start.

## Interface

The WPF interface uses a dark slate palette, translucent surfaces, rounded controls, vector sidebar icons, and Segoe UI Variable Text with a Segoe UI fallback.

- **1. Update OptiScaler** - one **Update** button, with **Open latest** and **Update Streamline** underneath. Detailed activity is collapsed until needed.
- **2. Install** - choose the actual game executable, a prepared nightly, and a load method. **Install** is the primary action. **Open folder** opens the game directory; **Edit OptiScaler.ini** opens the installed file directly in its associated editor.
- **Edit .ini** - separate groups for Upscaling, Frame generation, and GPU spoofing. Hardware-specific model controls are under **Models and default profile**.
- **Logs / setup** - session history, severity and text filters, and the GUI/CLI and download preferences.

The grouping follows OptiScaler's own upscaling and frame-generation input/output concepts. This is a separate application, not an official OptiScaler manager.

References used for the design and settings:

- https://github.com/optiscaler/OptiScaler
- https://github.com/optiscaler/OptiScaler/blob/master/OptiScaler.ini
- https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type
- https://learn.microsoft.com/en-us/windows/apps/design/style/mica

### Backdrop

The app uses dark controls throughout and requests native dark Mica using `DWMWA_SYSTEMBACKDROP_TYPE` and extends the DWM frame behind the content. When supported, the sidebar shows **Mica � Windows backdrop**. When unavailable, it uses a soft tinted background instead. Text remains opaque and readable; the entire window is not made translucent. Mica availability and appearance depend on Windows, composition, and the user's system settings.

## Editing an INI

1. Choose the prepared package, the installed game's INI, or another file.
2. Use **Quick controls** for common options, or **All settings** to edit every setting present in the selected file. Filter by section or search by key, value, or description.
3. Hover over a key or value to read the original multi-line and inline INI comments. Grouped settings share their preceding description. Keys without a description say so. Tooltips stay visible for up to one minute.
4. Use **Save changes** to write the changes. A `.bak` copy preserves the previous file.

Only edited keys are changed, in their own sections. Unrecognized values are shown as **Current: ...** and are retained unless changed. Other settings, comments, inline descriptions, spacing on untouched lines, and sections are preserved. Values entered in All settings synchronize with Quick controls. Searching or switching views retains unsaved changes.

**Predefined values** loads the application's built-in profile into the editor. It does not write the file until **Save changes** is used. Its defaults are:

| Setting | Predefined value |
| --- | --- |
| Upscaling DX11, DX12, Vulkan | Automatic |
| Frame generation enabled | Automatic |
| FG input | NvngxFG (`nvngxfg`) |
| FG output | FSR FG (`fsrfg`) |
| FG replacement | Combo |
| FSR backend/model and DLSS preset | Automatic |
| GPU spoofing | Automatic |

**Reload** discards staged values after confirmation and reloads the selected file. Under **Models and default profile**, **Use saved profile** stages the TOML profile instead of the built-in values; **Save as default profile** saves the current selections for future packages. An existing INI with custom spoofing switches can be saved directly; choose a named spoofing preset before saving a reusable TOML profile.

The editor explains invalid NvngxFG/XeFG or NvngxFG/DLSSG combinations inline and disables saving until they are corrected. This input requires FSR FG output in the supported nightly. FSR indices, FP8/INT8 support, and DLSS presets still depend on the GPU and installed libraries.

## Folder layout

| Path | Content |
| --- | --- |
| `source/streamline/` | Production Streamline x64 DLLs and integrity manifest |
| `source/dlssenabler/version.dll` | Copied as `OptiScaler/dlss-enabler-headless.dll` |
| `source/amd_files/` | Local AMD/FSR DLLs |
| `source/asi_loader/asiloader.dll` | x64 ASI Loader, installed as `version.dll` in ASI mode |
| `source/.cache/` | Verified downloads and application state |
| `output/<nightly>/` | Prepared nightly packages |
| `output/.logs/` | Readable session logs, JSONL events, and `latest-run.json` |
| `output/.backups/` | Installation backups and transaction records |
| `src/` | C# source, WPF theme, tests, and interface previews |

The previous `plugins`, `dados`, and `pacotes` layouts are migrated without overwriting conflicting files. Legacy on-disk package metadata names remain compatible. Old Portuguese-language session files are archived under `output/.logs/legacy/pre-english/`; they are preserved rather than rewritten as if they were new evidence.

## TOML and CLI

Keep **OptiScaler-Tools.toml** next to the executable. Existing values remain compatible.

```toml
[app]
gui = true
open_output = true

[game]
executable = 'D:\Games\My Game\Binaries\Win64\Game-Win64-Shipping.exe'
proxy = 'auto'
optipatcher = 'auto'
```

With `app.gui = true`, starting without arguments opens the graphical interface. With `false`, it opens the console and prepares the latest nightly. `--gui` forces the interface. Explicit commands use CLI mode.

`proxy` accepts `auto`, `dxgi`, `asi`, or `winmm`. `optipatcher` accepts `auto` or `off`. The `[ini]` values define the default profile used when preparing new packages. The supplied configuration documents every supported key. The parser supports these named tables, quoted strings, booleans, and comments; it is not a general-purpose TOML implementation. Unknown or duplicate keys and invalid values produce `E_CONFIG` with the line number.

In PowerShell, pipe the windowed EXE to `Out-Host` to wait for it:

```powershell
.\OptiScaler-Tools.exe --install --no-open --no-pause | Out-Host
.\OptiScaler-Tools.exe --game 'D:\Games\Game\Game.exe' --proxy auto --no-pause | Out-Host
.\OptiScaler-Tools.exe --streamline --no-pause | Out-Host
.\OptiScaler-Tools.exe --gui
.\OptiScaler-Tools.exe --organize --no-pause | Out-Host
.\OptiScaler-Tools.exe --self-test --no-pause | Out-Host
```

`--force`, `--clear-streamline`, `--menu`, `--dest`, and `--help` are also available. `--dest` changes the workspace base for `source/` and `output/`; the configuration is still read beside the application.

## Preparing and installing

**Update** checks `optiscaler/OptiScaler-nightly`, including pre-releases. Downloads are reused only when the size and the publisher's SHA-256, when provided, match. Large downloads use eight HTTP ranges with a single-connection fallback. ZIP is built in; 7z uses NanaZip, 7-Zip, or Windows tar.

Streamline is fetched from `NVIDIA-RTX/Streamline`. ARM/aarch64, x86/Win32, Linux, macOS, debug, source, and symbol packages are excluded. Only production DLLs directly in `bin/x64` are extracted. The cache is checked against asset identity and per-DLL hashes. A local fallback is reported as a warning; a fallback with known hash mismatches is rejected.

The package receives the local DLSS Enabler and AMD files, Streamline, and the selected TOML profile. The previous package is retained if preparation fails. Rebuilding the same nightly regenerates its files; manual modifications in that output folder may be replaced. Other nightly folders are retained.

On the Install page, select the actual Windows x64 game executable and close the game before installing. Automatic mode chooses the first available method:

1. `dxgi.dll`
2. `OptiScaler.asi` plus the ASI Loader as `version.dll`
3. `winmm.dll`

This priority checks file conflicts; it does not launch the game to test compatibility. Files belonging to another mod are kept. If no method is available, installation stops with `E_PROXY_CONFLICT`.

The application starts the bundled **setup_optiscaler.exe** helper without interactive prompts. This is a helper built by this project, implementing the equivalent file operations of the current nightly's `setup_windows.bat`; it is not an official upstream EXE, and the BAT is not blindly fed answers.

The helper stages the package, applies the current local files in `source`, verifies each copied file, records versions, and backs up replaced files. On failure it attempts to restore originals and remove new files. Backups and transaction records are retained. The game manifest, `OptiScaler-Tools.install.json`, includes versions, proxy, OptiPatcher status, hashes, and the backup location. When changing proxies, only files still matching the previous managed installation are removed.

OptiPatcher uses a positive executable-name match from the official `dllmain.cpp` checks before downloading the rolling ASI. When installed, `[Plugins] LoadAsiPlugins=true` is set. Unsupported games or unavailable downloads are reported and skipped. Name matching does not validate game-specific launch requirements or prove runtime compatibility.

Source: https://github.com/optiscaler/OptiPatcher

The helper does not start the game. Installations from a prepared package do not re-check the newest online release; use Update first if current components are required. **Edit OptiScaler.ini** on the Install page opens the game's actual INI directly.

## Installed games and Update all

**Installed games** in the sidebar maintains a persistent list of installations managed by this app. Successful installs are added automatically, with the executable path, OptiScaler and Streamline versions, loader, OptiPatcher preference, last update time, and per-game result code. Installations from earlier versions are recovered from successful session logs and the configured game when their installation manifests are still available. **Import installed** adds another existing installation by selecting its game executable and verifying its OptiScaler Tools manifest.

**Update all** checks and prepares the latest nightly once, then updates every registered game in sequence. Each game keeps its installed loader and OptiPatcher preference. Existing INI values, custom sections, and comments are retained; missing settings from the new package are added. Supported OptiPatcher installation may enable `LoadAsiPlugins` as required. Each game receives its own file backup and transaction. Close the games first.

A failed or missing game does not stop later games from updating. Per-game results remain visible, and logs record `BATCH_START`, `BATCH_GAME`, `BATCH_GAME_FAILED`, and `BATCH_COMPLETE`. Partial completion returns `E_BATCH_PARTIAL` / exit code 55. If preparing the new package fails, no game is updated. Games that have moved or lost their manifest remain listed as missing; import the new executable path and remove the obsolete entry.

Select a game to **Open folder**, **Edit INI**, or **Remove from list**. Removing a game does not delete or uninstall its files; it also prevents history discovery from automatically re-adding it. Explicit import adds it again. The list and its backup are saved locally at `source/.cache/installed-games.json` and `.json.bak`; keep that file when moving the app to another folder. It is excluded from GitHub and portable release packages.

Keep **both version 1.1 EXEs** together. Batch updates reject an older setup helper before launching it, because older helpers cannot preserve each game's INI.

CLI equivalents:

```powershell
.\OptiScaler-Tools.exe --games --no-pause | Out-Host
.\OptiScaler-Tools.exe --update-all --no-open --no-pause | Out-Host
```

## Logs

New application, installer, configuration, and test messages are in English. Common localized Windows file/network errors are presented with English descriptions, HRESULT/native codes, exception types, and stack information.

Each operation has a session ID, timestamp with time-zone offset, level, and event code. A readable `.log` and structured `.jsonl` are written to `output/.logs/`. **latest-run.json** records start/finish, success, warnings, versions, exit code, and the log path. Installer output is also forwarded to the parent UI session.

The log viewer can filter warnings/errors and search event text or codes. Historic sessions can be loaded from the session selector. Previous-language history is retained in the legacy archive.

| Exit code | Meaning |
| --- | --- |
| 0 | Completed; warnings are explicit |
| 10 | Configuration or arguments |
| 20-21 | Network or download integrity |
| 30 | Extraction/package |
| 40 | INI format or incompatible settings |
| 50-51 | Game path or proxy conflict |
| 52 | Permission denied |
| 53 | Copy/source integrity |
| 54 | Installed-games list, import or selection |
| 55 | Batch update completed with failed or missing games |
| 60-61 | Setup or optional patcher binary |
| 1 | Other I/O or unexpected error |

`ROLLBACK_OK` and `E_ROLLBACK` identify rollback results. `W_PATCHER_UNAVAILABLE` identifies an optional patcher problem. A successful copy is not reported as a successful game launch.

## Build and verification

For a source checkout, first copy `OptiScaler-Tools.example.toml` to `OptiScaler-Tools.toml` and place your local DLSS Enabler DLL at `source/dlssenabler/version.dll` before running `Build.ps1`. Runtime libraries, downloads, logs, and personal configuration are excluded from version control.

Keep both EXEs, the TOML, and `source/` together. The main EXE also embeds a DLSS Enabler fallback for package preparation. The ASI Loader remains external and is needed only for ASI installation.

`Build.ps1` uses the Windows .NET Framework 4.8 compiler, WPF, and the embedded `src/Theme.xaml`, builds both executables, and runs the engine and installer tests. No Python, browser, SDK, or third-party UI package is required. The executables are unsigned.

`--ui-test` renders every WPF page and verifies the update flow, section-aware editing, full-file editing, comment tooltips, search persistence, synchronization between views, inline validation, and staged predefined values. The engine tests cover architecture selection, cache integrity, TOML, extraction, migration, proxy order, ASI Loader, preserving other mods, rollback, and helper execution against synthetic game folders. API and patcher tests use simulated HTTP responses. These checks do not prove download availability or compatibility inside a running real game.
