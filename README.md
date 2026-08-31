# Strata

**See where your disk space actually went. Map any folder, rank the biggest files, find duplicate copies by content, and total up the folders you can rebuild — on macOS, Windows and Linux, with nothing to install. Your files never leave your device.**

👉 **Live:** https://rahulatrkm.github.io/strata/

👉 **Windows app:** [download the latest release](https://github.com/rahulatrkm/strata/releases/latest) —
one file, no installer, no runtime. It reads whole drives and can clean up,
always to the Recycle Bin. Source and details in [`desktop/`](desktop/README.md).

Every few months a drive fills up and the honest answer to "what is taking the
space?" turns out to be surprisingly hard to get. The good tools for this are
native, single-platform and usually paid; the free ones want an installer and a
kernel extension for a job that is really just *reading a directory*.

Strata is that job, in a browser tab. Point it at a folder and it walks
everything inside, then shows you:

- 🗺 **A treemap** — one block per file and folder, sized by what it takes up,
  click to go deeper. Backed by a real table, so it works with a keyboard and a
  screen reader too.
- 📦 **The largest files** — the twenty things you could delete once instead of
  a thousand things you delete forever.
- 👯 **Duplicates, compared byte for byte** — grouped by exact size, then a hash
  of the first 64 KB, then a hash of the whole file. A renamed copy is still
  caught, and two files that merely happen to be the same size are never
  called copies.
- 🧱 **Rebuildable folders** — `node_modules`, `__pycache__`, `DerivedData`,
  `.gradle`, `target` and friends, totalled up with the command that puts each
  one back.
- 🗂 **A breakdown by file type**, and 🕰 **large files untouched for a year**.
- ⬇ **CSV, JSON and clipboard exports**, because the useful thing to leave with
  is a list.
- 💾 **A download button that gives you the whole app as one file.** Keep it,
  read it, run it from your own disk forever.

## You don't have to trust the web page

A web page asking to read your disk is a reasonable thing to be suspicious of,
so Strata ships itself. Press **Download Strata** and you get a single
`strata.html` — the entire app, no installer, no dependencies, nothing to
unpack. Double-click it and it behaves exactly as the hosted version does, with
your Wi-Fi off if you like.

The downloaded copy is not the same bytes as the page:

- **The page-view counter is stripped out**, so it makes **no network requests
  at all**. Verified by opening the saved file with a request log attached:
  zero requests, zero external references.
- **It is captured before anything is scanned**, so it can never contain the
  names of your files.
- It flags itself with `data-offline="1"` and says on screen that it came off
  your disk rather than the internet.

## Why this instead of a native cleaner

|                        | Strata                         | Typical native cleaner        |
| ---------------------- | ------------------------------ | ----------------------------- |
| Platforms              | macOS, Windows, Linux          | usually one                   |
| Install                | none, or one saved HTML file   | download, install, notarise   |
| Price                  | free, MIT                      | usually paid                  |
| Duplicate detection    | content hash, three stages     | often name/size only, if any  |
| Sees your files        | never leaves the tab           | trust the vendor              |
| Deletes your files     | **never, deliberately**        | yes, often permanently        |
| Auditable              | one readable file you can keep | a signed binary               |

## What it will not do

This is the important half, and it is on the page as prominently as the features.

- **The web version never deletes a file.** A browser can only delete
  *permanently* — it cannot move anything to the Trash. A cleaner with no undo
  is not worth the gigabytes, so the page shows and counts, and you delete in
  your own file manager where the Trash still protects you.
- **The Windows app deletes only to the Recycle Bin**, refuses drives that have
  no Recycle Bin, and refuses Windows, System32, Program Files, ProgramData,
  drive roots, your profile folder and links. `File.Delete` appears nowhere in
  it, and a test scans the source to keep it that way.
- **Neither version uninstalls apps or removes their leftovers.** That means
  editing the registry or reaching into `~/Library`, and guessing at it is how
  a cleaner breaks software you still use.
- **Neither monitors CPU, memory or network.**
- **Neither will call a folder clean that it could not read.** Anything the
  operating system refused is counted, named and shown, and the total is
  labelled a floor rather than the truth.

## Why it's trustworthy

- **Nothing is uploaded.** There is no server and no account. Load the page,
  turn off your network, and everything still works.
- The one request the page makes has nothing to do with your files: a single
  anonymous "a page was opened" ping, with no identifier and nothing about your
  disk, honouring Do Not Track and Global Privacy Control. It is disclosed in
  the FAQ, and a test asserts every network call on the page lives inside that
  one block.
- **Nothing is written to browser storage** except your choice of GB or GiB.
- **Open source.** The whole app is one readable `index.html`.

## How it works

- **Chromium browsers** use the File System Access API (`showDirectoryPicker`),
  which also allows re-reading a folder in place after you have tidied it.
- **Firefox and Safari** fall back to `<input webkitdirectory>`. The map,
  duplicates, types and exports are all identical; you just pick the folder
  again rather than rescanning.
- **The treemap** is a squarified layout (Bruls, Huizing & van Wijk) drawn on a
  canvas — blocks stay close to square instead of degenerating into slivers, so
  the picture is readable at a glance.
- **Duplicate detection** is deliberately staged cheapest-first: size, then a
  64 KB head hash, then the full file. Only files agreeing at every stage are
  reported.

Browsers refuse access to the root of a system drive and to protected folders.
That restriction is exactly why this is safe to run, so Strata works with it:
point it at your home folder, Downloads, a projects directory or an external
drive.

## Tests

```
node strata.test.mjs                          # 116 checks, the web app
cd desktop && dotnet run --project Strata.Tests  # 70 checks, the desktop engine
cd desktop && dotnet run --project Strata.App -- --selftest  # 17 checks, the real window
```

The web checks run against the engine extracted from `index.html` itself so they
cannot drift from what ships. About half of all of it is about what Strata
refuses to claim — that two same-sized files are not duplicates, that files
sharing only their first 64 KB are not duplicates, that a junction is never
followed, that an unreadable folder is reported rather than dropped, that the
downloadable copy carries no beacon and no file names, and that the cleanup
guard will not let the operating system's own folders be deleted.

## Licence

MIT — see [LICENSE](LICENSE).
