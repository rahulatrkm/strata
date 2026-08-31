# Strata for Windows

The desktop build. It does the two things the browser version deliberately
cannot: read a **whole drive**, and **clean up**.

**Download:** [latest release](https://github.com/rahulatrkm/strata/releases/latest) —
one `Strata.exe`, about 133 MB, nothing to install and no .NET runtime needed.
The build is not code-signed yet, so SmartScreen will warn the first time; the
SHA-256 is published with the release.

## Why a native app at all

A browser can only see folders you hand it, and it can only delete
*permanently* — there is no Trash from a web page. A real application has
neither limit, which is what makes cleanup safe rather than frightening.

| | Web version | Windows app |
| --- | --- | --- |
| Whole drives | no, folders you pick | yes |
| Speed | one thread, browser APIs | parallel, native |
| Cleanup | never | yes, **Recycle Bin only** |
| Platforms | macOS, Windows, Linux | Windows x64 |
| Install | none | one file |

## The rules it will not break

- **Nothing is ever deleted permanently.** Removal goes through the shell with
  `FOF_ALLOWUNDO`, so everything lands in the Recycle Bin. `File.Delete` and
  `Directory.Delete` appear nowhere in the application, and a test scans the
  source to keep it that way.
- **`FOF_WANTNUKEWARNING` is set**, so if Windows cannot recycle something it
  must say so rather than destroy it quietly.
- **Drives without a Recycle Bin are refused** — network and removable drives,
  where a delete would be permanent.
- **The operating system is off limits**: Windows, System32, Program Files,
  ProgramData, the root of any drive, the folder holding user profiles, your
  profile root and the shell folders inside it. Contents of Documents are fair
  game; Documents itself is not.
- **Links are never followed and never deleted.** A junction pointing back up
  its own tree is an infinite tree; walking it hangs a scan and double-counts
  the bytes.
- **Nothing outside the folder you scanned can be touched.**
- **Anything unreadable is counted and named**, and the total is labelled a
  floor rather than the truth.

## Layout

- `Strata.Core` — scanning, tree, treemap, duplicates, rebuildable folders and
  the safety guard. Plain `net10.0` with no Windows types, so a macOS or Linux
  front end is a build rather than a rewrite.
- `Strata.App` — the WPF window, the treemap control and the Recycle Bin call.
- `Strata.Tests` — a console runner in the same style as the rest of this
  repository. No test framework, no packages.

## Building

```powershell
dotnet run --project Strata.Tests            # 70 checks
dotnet build Strata.App
dotnet run --project Strata.App -- --selftest # scans, lays out and renders headlessly
dotnet publish Strata.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None `
  -o publish\win-x64
```

`--selftest` runs the real scan, treemap layout and WPF render with no window on
screen and writes `strata-selftest-map.png` and `strata-selftest-window.png` to
your temp folder, so a build can be checked on a machine nobody is watching.

## macOS and Linux

Not yet, and not pretended otherwise. `Strata.Core` is platform-neutral and .NET
runs on both, so the work is a front end rather than a rewrite — but a download
that has never been built, signed or tested is not something worth shipping.
The [browser version](https://rahulatrkm.github.io/strata/) runs there today.
