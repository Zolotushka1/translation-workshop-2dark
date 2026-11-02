# 2Dark Translation Workshop

A local Windows desktop tool for translating **2Dark** and comparing the original English game with a Russian test build in one workspace. Built with C# and WPF on .NET 8. Current version: **0.4.0**. The application interface is currently in Russian; this documentation is in English.

This is an unofficial tool. You need your own supported copy of the game. Original game binaries and extracted original language tables are not included in Git.

## Features

- Edit 2,834 text occurrences across 11 canonical tables, preserving duplicate keys and file structure.
- Compare eight original language references and the legacy PG Team Russian translation; keep translator notes separately.
- Autosave private translation projects and validate variables, references and formatting commands.
- Run EN and RU test copies side by side with shared keyboard and mouse input.
- Synchronize logic steps using shared time, input snapshots and random seeds; the faster process waits for the slower process.
- Apply text changes to the running RU process and refresh open menus without restarting.
- Save/restore native level snapshots, load both sides in parallel, and align RU to EN.
- Pause/resume, open the native menu, capture screenshots, and compare fullscreen views.
- Configure fullscreen shortcuts, version label position/color/opacity/size, and a single audio source.
- Use legacy PG Team Cyrillic fonts for testing.

## Requirements

- Windows x64, Steam, and a supported original 32-bit 2Dark installation.
- .NET 8 Desktop Runtime x64 to run; a .NET SDK capable of building `net8.0-windows` to build. Development used SDK 9.0.305.
- Git and a local copy of QuickBMS for initial extraction and integration checks.
- Disk space for two complete test copies, extracted text/scripts, and optional snapshots.

Native integration supports the exact build recorded in `data/supported-build.json`. The supported EXE SHA-256 is `b4f82c4bc414d5e88ae231b4da9b066579ae9945cfb773d79ee96b651b837a73`. Setup rejects modified/unsupported files before extraction. Other stores and versions have not been validated.

## Setup and build

Clone anywhere on your computer; the game directory is configured locally. Download QuickBMS from its [official page](https://aluigi.altervista.org/quickbms.htm), extract it, and run these commands in PowerShell from the repository root, substituting your paths:

```powershell
./scripts/Setup.ps1 -GamePath 'D:\SteamLibrary\steamapps\common\2Dark' -QuickBms 'C:\Tools\quickbms\quickbms.exe'
./scripts/Build.ps1
./scripts/Start.ps1
```

Setup verifies the expected EXE/archive hashes, extracts language tables, font descriptors and scripts into ignored local folders, and writes an ignored `game.path.json`. It does not change the installed game or import Steam saves. Build output goes to `workshop/app/`.

## Basic workflow

1. Select an entry and edit the Russian target. Enter inserts `{CR}`. Preserve protected variables, references and icon tags.
2. Choose **Запустить синхронно** (Launch synchronized) to prepare both test copies. Existing RU profile data is backed up before alignment with EN.
3. Click a game panel to control both games. Typing in the editor is not forwarded to them.
4. **Перевод на лету** (Live translation) applies edits to loaded RU tables. **Применить текст** applies/refreshes manually. F5 persists archive changes in the test copy.
5. **Сохранить сцену** / **Загрузить сцену** saves/restores a level. In synchronized mode both sides load the EN state, falling back to RU if needed. The first ready side waits for the other.

New projects start without the maintainer's personal translation. PG Team text is a reference; **Взять старый перевод** copies only the selected entry. Empty targets fall back to English. Projects, notes, review status and backups under `projects/` are excluded from Git.

## Controls

| Control | Action |
| --- | --- |
| Ctrl+S | Save project |
| Ctrl+F | Search entries |
| F5 | Rebuild and launch RU |
| F11 | Enter/leave fullscreen (configurable) |
| Tab in fullscreen | Switch EN/RU (configurable) |
| Пауза обеих | Pause/resume both simulations |
| Esc / меню | Send Escape through game input |
| Снимок ENG / Снимок RU | Save a screenshot without changing pause state |
| Выкл. / Выкл. обе | Shut down one/both test copies |

Fullscreen labels show an exit hint in the selected version's language. Unchecking labels or single-source audio hides their related settings. Closing the editor releases input hooks and detaches running test windows; use **Выкл. обе** first to terminate both games.

## Limits and validation

Translations can exceed the original string length. Live updates apply to edited entries in active tables. Font textures and other assets require rebuilding/restarting. PG Team's menu font has uppercase Cyrillic glyphs; lowercase characters reuse those glyphs. A new font generator is not included.

Snapshots use the engine's level serialization, not full process memory. Main menus/startup cards cannot be saved this way. Close native menus/documents and wait for transitions before saving/restoring. Snapshot UI state is not preserved.

Synchronized logic targets 60 steps per simulated second and waits for both processes. Rendering and audio devices remain independent. Menus and Smith's home were tested; one test produced byte-identical native states after 4,561 shared steps. A complete playthrough and every level transition have **not** been validated.

The local integration suite contains 76 checks. It requires setup, the supported game, and no running RU test process:

```powershell
dotnet run --project workshop/Workshop.Checks -c Release -- .
```

The suite rebuilds the ignored RU runtime and writes a sample translation there; rebuild from your own project afterwards. Private test reports/snapshots are not shipped. You can compile without game data:

```powershell
dotnet build workshop/Workshop.App -c Release
```

## Repository layout

- `workshop/Workshop.App`: WPF editor, game hosting, input and audio.
- `workshop/Workshop.Core`: archives, corpus, projects and native integration.
- `workshop/Workshop.Checks`: local integration checks.
- `workshop/Workshop.Probe`: diagnostics for the workshop's test processes.
- `scripts/`: setup, build and launch helpers.
- `data/`: corpus metadata and supported file hashes, without game text.
- `old_extracted/`, `old_payload/`: PG Team reference assets.
- `sources/`: extraction script.

Generated `original/`, `runtime/`, `tools/`, `projects/`, `checks/`, screenshots, saves and machine configuration are ignored. Do not force-add those folders.

## Credits and licensing

Legacy Russian localization and Cyrillic assets: **PG Team**. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for sources and included files. The legacy reference is separate from any new translation.

The workshop's original source code and documentation are licensed under the [MIT License](LICENSE). This does not cover PG Team assets, the third-party extraction script, or game content; see [LICENSE_SCOPE.md](LICENSE_SCOPE.md). This tool is not affiliated with the game's developers, publisher, Steam or PG Team.
