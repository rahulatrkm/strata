# Strata

**See where your disk space actually went. Map any folder, rank the biggest files, find duplicate copies by content, and total up the folders you can rebuild — on macOS, Windows and Linux, with nothing to install. Your files never leave your device.**

👉 **Live:** https://rahulatrkm.github.io/strata/

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

## Why this instead of a native cleaner

|                        | Strata                         | Typical native cleaner        |
| ---------------------- | ------------------------------ | ----------------------------- |
| Platforms              | macOS, Windows, Linux          | usually one                   |
| Install                | none — it is a web page        | download, install, notarise   |
| Price                  | free, MIT                      | usually paid                  |
| Duplicate detection    | content hash, three stages     | often name/size only, if any  |
| Sees your files        | never leaves the tab           | trust the vendor              |
| Deletes your files     | **never, deliberately**        | yes, often permanently        |

## What it will not do

This is the important half, and it is on the page as prominently as the features.

- **It never deletes a file.** A browser can only delete *permanently* — it
  cannot move anything to the Trash. A cleaner with no undo is not worth the
  gigabytes, so Strata shows and counts, and you delete in your own file
  manager where the Trash still protects you.
- **It does not uninstall apps or remove their leftovers.** That means reaching
  into `~/Library` or the Windows registry, which a web page cannot and should
  not be able to do.
- **It does not monitor CPU, memory or network.** A browser cannot read them.
- **It will not call a folder clean that it could not read.** Anything the
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
node strata.test.mjs
```

91 checks, run against the engine extracted from `index.html` itself so they
cannot drift from what ships. About half are about what Strata refuses to
claim — that two same-sized files are not duplicates, that files sharing only
their first 64 KB are not duplicates, that an unreadable folder is reported
rather than dropped, and that the page never promises the deleting and system
monitoring a browser cannot do.

## Licence

MIT — see [LICENSE](LICENSE).
