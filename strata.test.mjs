// Headless harness for Strata's scanning, treemap and duplicate-detection engine.
//
// The engine is read straight out of index.html and run in a sandbox with no
// `document`, so these tests exercise the code that actually ships rather than
// a copy that can drift away from it.
//
// A disk tool earns trust by being right about two things: the number it shows
// you, and the things it refuses to say. So roughly half of what follows is
// about the second kind — that a folder it could not read is reported instead
// of quietly missing from the total, that two different files of the same size
// are never called duplicates, and that the page does not promise the deleting
// and system monitoring a browser cannot do.
import fs from "node:fs";
import vm from "node:vm";

const FILE = process.argv[2] || new URL("./index.html", import.meta.url);
const html = fs.readFileSync(FILE, "utf8");
const js = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1]).join("\n");

const sandbox = {
  console, Math, Date, JSON, Object, Array, String, Number, Error, Set, Map, Promise,
  Infinity, NaN, isFinite, isNaN, parseInt, parseFloat, RegExp, Symbol,
  TextEncoder, TextDecoder, Uint8Array, ArrayBuffer, DataView, Blob, crypto,
  setTimeout, clearTimeout,
};
sandbox.globalThis = sandbox;
sandbox.window = sandbox;
vm.createContext(sandbox);
vm.runInContext(js, sandbox, { filename: "strata.js" });

const g = n => vm.runInContext(n, sandbox);
const formatBytes = g("formatBytes");
const extOf = g("extOf");
const categoryOf = g("categoryOf");
const buildTree = g("buildTree");
const pathOf = g("pathOf");
const filePath = g("filePath");
const allFiles = g("allFiles");
const childrenOf = g("childrenOf");
const squarify = g("squarify");
const topFiles = g("topFiles");
const staleFiles = g("staleFiles");
const byCategory = g("byCategory");
const dominantCategory = g("dominantCategory");
const rebuildableFolders = g("rebuildableFolders");
const findDuplicates = g("findDuplicates");
const scanDirectory = g("scanDirectory");
const entriesFromFileList = g("entriesFromFileList");
const rootNameFromFileList = g("rootNameFromFileList");
const summarise = g("summarise");
const toCsv = g("toCsv");

let pass = 0, fail = 0;
const group = t => console.log(`\nSTRATA — ${t}`);
const ok = (name, cond, detail) => {
  if (cond) { pass++; console.log(`  PASS  ${name}${detail ? "  " + detail : ""}`); }
  else { fail++; console.log(`  FAIL  ${name}${detail ? "  " + detail : ""}`); }
};
const near = (a, b, eps = 1e-6) => Math.abs(a - b) <= eps;

/* ============================================================ sizes */

group("byte sizes, in both the units people actually see");
{
  ok("bytes stay bytes", formatBytes(0) === "0 B" && formatBytes(999) === "999 B",
    `${formatBytes(0)}, ${formatBytes(999)}`);
  ok("decimal rolls over at 1000", formatBytes(1000) === "1.00 KB", formatBytes(1000));
  ok("binary rolls over at 1024", formatBytes(1024, true) === "1.00 KiB", formatBytes(1024, true));
  // The same disk, reported two ways: this is exactly why Windows and macOS
  // disagree about the size of the same drive, so both must be available.
  ok("a real drive reads differently in each", formatBytes(500e9) === "500 GB" && formatBytes(500e9, true) === "466 GiB",
    `${formatBytes(500e9)} vs ${formatBytes(500e9, true)}`);
  ok("precision tightens as numbers grow",
    formatBytes(1234) === "1.23 KB" && formatBytes(12345) === "12.3 KB" && formatBytes(123456) === "123 KB",
    [formatBytes(1234), formatBytes(12345), formatBytes(123456)].join(" / "));
  ok("nonsense is not dressed up as a number",
    formatBytes(-5) === "\u2014" && formatBytes(NaN) === "\u2014" && formatBytes(undefined) === "\u2014");
}

/* ============================================================ classification */

group("working out what a file is");
{
  ok("ordinary extensions", extOf("clip.MP4") === "mp4" && extOf("notes.txt") === "txt");
  ok("a dotfile has no extension", extOf(".gitignore") === "" && extOf(".env") === "");
  ok("a trailing dot is not an extension", extOf("weird.") === "");
  ok("only the last extension counts", extOf("archive.tar.gz") === "gz");
  ok("junk after a dot is not treated as one", extOf("v1.2 final draft") === "",
    JSON.stringify(extOf("v1.2 final draft")));
  ok("video, photos and code land in the right buckets",
    categoryOf("a.mkv") === "Video" && categoryOf("b.heic") === "Photos & images" &&
    categoryOf("c.rs") === "Code & config");
  ok("anything unrecognised is called Other, not guessed",
    categoryOf("mystery.qqq") === "Other" && categoryOf("README") === "Other");
}

/* ============================================================ the tree */

const SAMPLE = [
  { path: "Movies/holiday.mp4", size: 4_000_000_000, mtime: 1_700_000_000_000 },
  { path: "Movies/old/wedding.mov", size: 2_000_000_000, mtime: 1_400_000_000_000 },
  { path: "Photos/img1.jpg", size: 3_000_000, mtime: 1_700_000_000_000 },
  { path: "Photos/img2.jpg", size: 5_000_000, mtime: 1_700_000_000_000 },
  { path: "code/app/node_modules/left-pad/index.js", size: 900_000, mtime: 1_700_000_000_000 },
  { path: "code/app/node_modules/react/index.js", size: 1_100_000, mtime: 1_700_000_000_000 },
  { path: "code/app/src/main.js", size: 20_000, mtime: 1_700_000_000_000 },
  { path: "notes.txt", size: 1_000, mtime: 1_700_000_000_000 },
];

group("turning a flat list of paths back into folders");
{
  const tree = buildTree(SAMPLE, "Home");
  const total = SAMPLE.reduce((a, b) => a + b.size, 0);

  ok("every byte is accounted for", tree.size === total, `${tree.size} vs ${total}`);
  ok("every file is accounted for", tree.count === SAMPLE.length, `${tree.count}`);
  ok("folders nest", tree.dirs.get("code").dirs.get("app").dirs.has("node_modules"));
  ok("a folder totals everything beneath it",
    tree.dirs.get("code").size === 900_000 + 1_100_000 + 20_000, String(tree.dirs.get("code").size));
  ok("files at the root are not lost", tree.files.some(f => f.name === "notes.txt"));
  ok("paths rebuild exactly",
    pathOf(tree.dirs.get("code").dirs.get("app").dirs.get("node_modules")) === "code/app/node_modules");
  ok("a file knows its own full path",
    allFiles(tree).some(f => filePath(f) === "code/app/node_modules/react/index.js"));
  ok("children come back biggest first",
    childrenOf(tree)[0].name === "Movies" && childrenOf(tree)[0].isDir);
  ok("the largest file is found", topFiles(tree, 1)[0].size === 4_000_000_000);
}

/* ============================================================ treemap */

group("the treemap covers the box exactly");
{
  const items = [
    { name: "a", size: 600 }, { name: "b", size: 300 }, { name: "c", size: 60 },
    { name: "d", size: 30 }, { name: "e", size: 8 }, { name: "f", size: 2 },
  ];
  const W = 800, H = 420;
  const laid = squarify(items, 0, 0, W, H);

  ok("every item gets a rectangle", laid.length === items.length, `${laid.length}`);
  const area = laid.reduce((a, r) => a + r.w * r.h, 0);
  ok("the rectangles tile the whole box", near(area, W * H, 1e-3), `${area.toFixed(2)} vs ${W * H}`);

  // A block twice the size must be drawn twice as large, or the picture lies.
  const total = items.reduce((a, b) => a + b.size, 0);
  const proportional = laid.every(r => near((r.w * r.h) / (W * H), r.item.size / total, 1e-9));
  ok("each area is proportional to its size", proportional);

  const inside = laid.every(r => r.x >= -1e-9 && r.y >= -1e-9 && r.x + r.w <= W + 1e-9 && r.y + r.h <= H + 1e-9);
  ok("nothing is drawn outside the box", inside);

  const overlap = (p, q) => p.x < q.x + q.w - 1e-9 && q.x < p.x + p.w - 1e-9 &&
                            p.y < q.y + q.h - 1e-9 && q.y < p.y + p.h - 1e-9;
  let clashes = 0;
  for (let i = 0; i < laid.length; i++) for (let j = i + 1; j < laid.length; j++) if (overlap(laid[i], laid[j])) clashes++;
  ok("no two blocks overlap", clashes === 0, `${clashes} overlaps`);

  // The point of squarifying is readable blocks. Slice-and-dice would give the
  // smallest item an aspect ratio in the hundreds; this must be far better.
  const worst = Math.max(...laid.map(r => Math.max(r.w / r.h, r.h / r.w)));
  ok("blocks stay roughly square rather than becoming slivers", worst < 12, `worst ratio ${worst.toFixed(1)}`);

  ok("an empty folder lays out to nothing", squarify([], 0, 0, W, H).length === 0);
  ok("zero-byte files are not given a rectangle",
    squarify([{ name: "z", size: 0 }, { name: "y", size: 5 }], 0, 0, 100, 100).length === 1);
  ok("a single item fills the box", near(squarify([{ name: "a", size: 9 }], 0, 0, 40, 25)[0].w * squarify([{ name: "a", size: 9 }], 0, 0, 40, 25)[0].h, 1000, 1e-6));
  ok("a zero-sized box is refused rather than dividing by zero",
    squarify(items, 0, 0, 0, 100).length === 0 && squarify(items, 0, 0, 100, 0).length === 0);
}

/* ============================================================ duplicates */

const enc = new TextEncoder();
const fakeDir = { name: "", parent: null };
const mkFile = (name, bytes) => ({ name, size: bytes.length, content: bytes, dir: fakeDir, mtime: 0 });

const testIo = {
  async read(file, start, end){
    if (file.broken) { const e = new Error("nope"); e.name = "NotAllowedError"; throw e; }
    return file.content.slice(start, end).buffer;
  },
  async hash(buf){
    const digest = await crypto.subtle.digest("SHA-256", buf);
    return [...new Uint8Array(digest)].map(b => b.toString(16).padStart(2, "0")).join("");
  },
};

const filler = (n, seed) => {
  const out = new Uint8Array(n);
  for (let i = 0; i < n; i++) out[i] = (i * 31 + seed) & 255;
  return out;
};

group("duplicate files, proved byte by byte");
{
  const a = mkFile("holiday.jpg", enc.encode("x".repeat(9000)));
  const b = mkFile("holiday (copy).jpg", enc.encode("x".repeat(9000)));
  const c = mkFile("different.jpg", enc.encode("y".repeat(9000)));

  const { groups } = await findDuplicates([a, b, c], testIo, { headBytes: 4096 });
  ok("identical files are grouped whatever they are called", groups.length === 1 && groups[0].copies === 2,
    `${groups.length} group(s)`);
  ok("the reclaimable figure keeps one copy", groups[0].wasted === 9000, String(groups[0].wasted));

  // Same size, different contents. A tool that groups on size alone — which is
  // the cheap way to build this — fails right here.
  const sameSize = await findDuplicates([a, c], testIo, { headBytes: 4096 });
  ok("two different files of identical size are never called duplicates", sameSize.groups.length === 0,
    `${sameSize.groups.length} group(s)`);
}

group("the full-file pass actually runs");
{
  // Both files are 80 KB and share their first 64 KB exactly, so the head hash
  // matches and only reading to the end can tell them apart. Without the third
  // stage this pair would be reported as duplicates and someone would delete a
  // file that was not a copy.
  const head = filler(65536, 1);
  const one = new Uint8Array(81920); one.set(head, 0); one.set(filler(16384, 2), 65536);
  const two = new Uint8Array(81920); two.set(head, 0); two.set(filler(16384, 99), 65536);

  const shared = await findDuplicates([mkFile("one.bin", one), mkFile("two.bin", two)], testIo, { headBytes: 65536 });
  ok("files sharing only their first 64 KB are not duplicates", shared.groups.length === 0,
    `${shared.groups.length} group(s)`);

  const identical = new Uint8Array(one);
  const both = await findDuplicates([mkFile("one.bin", one), mkFile("copy.bin", identical)], testIo, { headBytes: 65536 });
  ok("and files matching all the way to the end still are", both.groups.length === 1 && both.groups[0].copies === 2);
}

group("what the duplicate scan refuses to do");
{
  const tiny1 = mkFile("a.txt", enc.encode("hi"));
  const tiny2 = mkFile("b.txt", enc.encode("hi"));
  const small = await findDuplicates([tiny1, tiny2], testIo, { minSize: 4096 });
  ok("tiny files are skipped, since deleting them reclaims nothing", small.groups.length === 0);

  const good = mkFile("good.bin", filler(9000, 3));
  const bad = mkFile("locked.bin", filler(9000, 3));
  bad.broken = true;
  const mixed = await findDuplicates([good, bad], testIo, { headBytes: 4096 });
  ok("a file it could not read is reported, not silently dropped",
    mixed.unreadable.length === 1 && mixed.unreadable[0].path === "locked.bin",
    JSON.stringify(mixed.unreadable));
  ok("and it is not counted as a duplicate of anything", mixed.groups.length === 0);

  const stopped = await findDuplicates(
    [mkFile("p.bin", filler(9000, 4)), mkFile("q.bin", filler(9000, 4))],
    testIo, { headBytes: 4096, shouldStop: () => true });
  ok("stopping mid-comparison returns early instead of hanging", stopped.groups.length === 0);
}

/* ============================================================ rebuildable */

group("folders a tool can put back");
{
  const tree = buildTree(SAMPLE, "Home");
  const found = rebuildableFolders(tree);
  ok("node_modules is found", found.some(f => f.path === "code/app/node_modules"), JSON.stringify(found.map(f => f.path)));
  ok("it carries the command that recreates it", found[0].how === "npm install", found[0].how);
  ok("and the size of everything inside it", found[0].size === 2_000_000, String(found[0].size));

  // A package inside node_modules that is itself called `build` must not be
  // added again, or the headline total counts the same bytes twice.
  const nested = buildTree([
    { path: "app/node_modules/pkg/build/out.js", size: 100, mtime: 0 },
    { path: "app/node_modules/pkg/index.js", size: 50, mtime: 0 },
  ], "Home");
  const nestedFound = rebuildableFolders(nested);
  ok("nested matches are not counted twice",
    nestedFound.length === 1 && nestedFound[0].size === 150,
    JSON.stringify(nestedFound.map(f => `${f.path}=${f.size}`)));

  ok("guessier names are labelled as needing a check",
    rebuildableFolders(buildTree([{ path: "proj/dist/a.js", size: 10, mtime: 0 }], "H"))[0].confidence === "medium");
}

/* ============================================================ stale + types */

group("old files and file types");
{
  const now = Date.UTC(2026, 0, 1);
  const day = 86400000;
  const tree = buildTree([
    { path: "big-old.mov", size: 50_000_000, mtime: now - 400 * day },
    { path: "big-new.mov", size: 50_000_000, mtime: now - 10 * day },
    { path: "small-old.txt", size: 500, mtime: now - 400 * day },
    { path: "no-date.bin", size: 50_000_000, mtime: 0 },
  ], "Home");

  const stale = staleFiles(tree, 365, now);
  ok("only large, genuinely old files are listed",
    stale.length === 1 && stale[0].name === "big-old.mov", JSON.stringify(stale.map(f => f.name)));
  ok("a file with no usable date is left out rather than guessed at",
    !stale.some(f => f.name === "no-date.bin"));

  const cats = byCategory(buildTree(SAMPLE, "Home"));
  ok("categories total correctly and sort by size",
    cats[0].category === "Video" && cats[0].size === 6_000_000_000, JSON.stringify(cats[0]));
  ok("every file lands in exactly one category",
    cats.reduce((a, c) => a + c.files, 0) === SAMPLE.length);
}

group("a folder is coloured by what is actually inside it");
{
  // Every folder drawn in the same colour was the first version of the map, and
  // it made the picture useless: you could see where the space went but not
  // what it was. A folder now takes the colour of the kind of file filling it.
  const tree = buildTree(SAMPLE, "Home");
  ok("a folder of video reads as video", dominantCategory(tree.dirs.get("Movies")) === "Video");
  ok("a folder of source reads as code", dominantCategory(tree.dirs.get("code")) === "Code & config");
  ok("a folder of pictures reads as pictures", dominantCategory(tree.dirs.get("Photos")) === "Photos & images");

  // Size decides, not the number of files: one huge video outweighs many scripts.
  const mixed = buildTree([
    { path: "mixed/a.js", size: 1000, mtime: 0 },
    { path: "mixed/b.js", size: 1000, mtime: 0 },
    { path: "mixed/c.js", size: 1000, mtime: 0 },
    { path: "mixed/film.mp4", size: 900_000, mtime: 0 },
  ], "Home");
  ok("the biggest kind wins, not the most numerous",
    dominantCategory(mixed.dirs.get("mixed")) === "Video");

  ok("an empty folder still gets a colour rather than crashing",
    dominantCategory(buildTree([{ path: "x/y.txt", size: 1, mtime: 0 }], "H").dirs.get("x")) === "Documents");

  const tricky = buildTree(SAMPLE, "Home").dirs.get("Movies");
  const first = dominantCategory(tricky);
  ok("the answer is cached and stays the same on redraw", dominantCategory(tricky) === first);

  const g2 = g("CATEGORY_COLOUR");
  ok("every category the map can produce has a colour",
    [...Object.keys(g("CATEGORY_EXT")), "Other"].every(c => !!g2[c]),
    JSON.stringify([...Object.keys(g("CATEGORY_EXT")), "Other"].filter(c => !g2[c])));
}

/* ============================================================ scanning */

const dirHandle = (name, children) => ({
  kind: "directory", name,
  async *values(){ for (const c of children) yield c; },
});
const fileHandle = (name, size, mtime = 0) => ({
  kind: "file", name,
  async getFile(){ return { size, lastModified: mtime }; },
});
const deniedDir = name => ({
  kind: "directory", name,
  async *values(){ const e = new Error("denied"); e.name = "NotAllowedError"; throw e; },
});
const deniedFile = name => ({
  kind: "file", name,
  async getFile(){ const e = new Error("denied"); e.name = "NotAllowedError"; throw e; },
});

group("walking a real folder handle");
{
  const root = dirHandle("Home", [
    fileHandle("a.txt", 100, 5),
    dirHandle("sub", [fileHandle("b.bin", 900, 6), dirHandle("deep", [fileHandle("c.mp4", 4000, 7)])]),
  ]);
  const scan = await scanDirectory(root);
  ok("every file is found however deep", scan.files === 3, String(scan.files));
  ok("bytes are totalled", scan.bytes === 5000, String(scan.bytes));
  ok("paths are relative to the folder you picked",
    scan.entries.map(e => e.path).sort().join(",") === "a.txt,sub/b.bin,sub/deep/c.mp4",
    scan.entries.map(e => e.path).sort().join(","));
  ok("folders are counted", scan.dirs === 3, String(scan.dirs));
  ok("a clean scan reports nothing skipped", scan.skipped.length === 0);

  const model = summarise(scan, "Home");
  ok("the summary agrees with the tree it built", model.bytes === 5000 && model.files === 3);
  ok("and knows the largest file", model.biggest === 4000);
}

group("a folder it is refused is never called empty");
{
  // The failure this guards against: the OS denies one folder, the tool ignores
  // the error, and the user is told their disk holds less than it does.
  const root = dirHandle("Home", [
    fileHandle("visible.txt", 100),
    deniedDir("Library"),
    deniedFile("locked.key"),
  ]);
  const scan = await scanDirectory(root);
  ok("what could be read is still reported", scan.files === 1 && scan.bytes === 100);
  ok("both refusals are recorded", scan.skipped.length === 2, JSON.stringify(scan.skipped));
  ok("with a reason a person can act on",
    scan.skipped.every(s => s.reason === "permission denied"), JSON.stringify(scan.skipped.map(s => s.reason)));
  ok("the summary carries the refusals through to the page",
    summarise(scan, "Home").skipped.length === 2);
}

group("stopping a long scan");
{
  const root = dirHandle("Home", [dirHandle("a", [fileHandle("x", 1)]), dirHandle("b", [fileHandle("y", 1)])]);
  const scan = await scanDirectory(root, { shouldStop: () => true });
  ok("it stops when asked", scan.stopped === true);
  ok("and says so, so the totals are not mistaken for complete", summarise(scan, "H").stopped === true);
}

group("the picker every browser has");
{
  const list = [
    { webkitRelativePath: "Pictures/2024/a.jpg", size: 10, lastModified: 1 },
    { webkitRelativePath: "Pictures/b.jpg", size: 20, lastModified: 2 },
  ];
  const entries = entriesFromFileList(list);
  ok("the folder you picked is stripped from the paths",
    entries.map(e => e.path).join(",") === "2024/a.jpg,b.jpg", entries.map(e => e.path).join(","));
  ok("sizes and dates survive", entries[0].size === 10 && entries[0].mtime === 1);
  ok("the blob is kept so duplicates can still be hashed", !!entries[0].blob);
  ok("the folder name is recovered for the heading", rootNameFromFileList(list) === "Pictures");
  ok("a tree built from the fallback matches one built from a handle",
    buildTree(entries, "Pictures").size === 30);
}

/* ============================================================ export */

group("the exported list survives a spreadsheet");
{
  ok("commas and quotes are escaped",
    toCsv([["a,b", 'say "hi"']]) === '"a,b","say ""hi"""', toCsv([["a,b", 'say "hi"']]));
  ok("a newline inside a filename does not break the row",
    toCsv([["line\nbreak", 1]]) === '"line\nbreak",1');
  ok("ordinary values are left alone", toCsv([["plain", 42]]) === "plain,42");
}

/* ============================================================ the claims */

group("the copy you can download and keep");
{
  // The point of the saved file is that someone who does not want to trust a
  // web page does not have to. So it has to be the whole app, and it has to be
  // provably incapable of talking to anything.
  const offlineDocument = g("offlineDocument");
  const saved = offlineDocument(html);

  ok("the page offers a download at all", /id="save"/.test(html) && /Download Strata/.test(html));
  ok("the saved copy drops the view counter",
    !/data-count="strata"/.test(saved) && !/portfolio-likes/.test(saved));
  ok("and with it every last network call",
    (saved.match(/\bfetch\s*\(|XMLHttpRequest|sendBeacon|WebSocket|EventSource/g) || []).length === 0,
    JSON.stringify(saved.match(/\bfetch\s*\(|XMLHttpRequest|sendBeacon|WebSocket|EventSource/g) || []));
  ok("it marks itself as the offline copy", /<html[^>]*\sdata-offline="1"/.test(saved),
    (saved.match(/<html[^>]*>/) || [""])[0]);
  ok("it says so on screen when opened", /id="offlineNote"/.test(saved));

  // A copy missing the engine or the styles would open to a broken page.
  ok("it still carries the whole engine",
    saved.includes("function squarify") && saved.includes("function findDuplicates") &&
    saved.includes("function scanDirectory"));
  ok("it still carries its styles and markup",
    saved.includes("<style>") && saved.includes('id="map"') && saved.includes('id="dirInput"'));
  ok("it needs nothing from the network to render",
    !/<(?:script[^>]+src|link[^>]+stylesheet|img[^>]+src)=[^>]*https?:/i.test(saved));
  ok("it is a complete document", /^<!DOCTYPE html>/i.test(offlineDocument("<!DOCTYPE html>\n<html><body></body></html>")));

  // Captured before anything is scanned, so it cannot carry a file listing.
  ok("the copy is taken before any scan touches the page",
    /PRISTINE_HTML[\s\S]{0,400}?document\.documentElement\.outerHTML/.test(js) &&
    js.indexOf("PRISTINE_HTML") < js.indexOf("function ui(") ,
    "must be captured at the top of the script, not on demand");
  ok("the results panels it captures are empty",
    /<tbody><\/tbody>/.test(saved.replace(/\s+/g, "")) || /id="dupOut"><\/div>/.test(saved.replace(/\s+/g, "")));
}

group("the page only claims what a browser can actually do");
{
  const text = html.replace(/<[^>]+>/g, " ").replace(/\s+/g, " ");

  ok("it says plainly that this page never deletes", /never deletes|does not delete|not delete/i.test(text));
  ok("it explains why deleting from a browser would be unsafe",
    /cannot move anything to the Trash|delete permanently|only delete permanently/i.test(text));
  ok("neither version claims to uninstall apps",
    /neither version uninstalls|does not uninstall/i.test(text));
  ok("it does not promise CPU, memory or network monitoring",
    /does not monitor|cannot read them/i.test(text) && !/real-?time monitoring/i.test(text));
  ok("it warns that a rebuildable folder is a name match, not proof",
    /name match, not|not proof|not a promise/i.test(text));
  ok("it tells you the total is a floor when something could not be read",
    /floor, not/i.test(html));
  ok("it is honest that the whole drive is off limits",
    /root of a system drive|refuse access/i.test(text));
}

group("the Windows app is offered honestly");
{
  const text = html.replace(/<[^>]+>/g, " ").replace(/\s+/g, " ");

  ok("the page links to the Windows download",
    /releases\/latest\/download\/Strata\.exe/.test(html));
  ok("and to the release notes, where the checksum is",
    /releases\/latest["'][^>]*>[^<]*(?:notes|checksum)/i.test(html) || /Release notes and checksum/i.test(text));
  ok("it warns that the build is not code-signed",
    /not\s+code-signed|SmartScreen/i.test(text),
    "an unsigned download that surprises you with a warning looks like malware");
  ok("it says the desktop cleanup goes to the Recycle Bin, not oblivion",
    /Recycle Bin/i.test(text));
  ok("and that drives without a Recycle Bin are refused",
    /refuses drives that have no Recycle Bin|no Recycle Bin/i.test(text));

  // The one thing not to fake: there is no Mac build, and saying otherwise
  // would be exactly the overclaiming this product is meant to avoid.
  ok("it admits there is no macOS build rather than implying one",
    /macOS and Linux: not yet|not yet/i.test(text) && !/download for mac/i.test(text));
  ok("and points macOS users at the version that does work today",
    /runs there today|version on this page/i.test(text));
}

group("nothing about your files leaves the page");
{
  const scripts = [...html.matchAll(/<script\b[^>]*>/g)].map(m => m[0]);
  const external = scripts.filter(s => /\bsrc=/.test(s));
  ok("no third-party script is loaded", external.length === 0, external.join(" "));
  ok("no external stylesheet or webfont",
    !/<link[^>]+stylesheet/i.test(html) && !/fonts\.googleapis|fonts\.gstatic|@import/i.test(html));
  ok("no image is fetched from a host", !/<img[^>]+src=["']https?:/i.test(html));

  // One anonymous view ping is the only request the page makes, it is confined
  // to its own script block, and the FAQ says so in as many words.
  const engine = js;
  const NETWORK = /\bfetch\s*\(|XMLHttpRequest|sendBeacon|WebSocket|EventSource/g;
  ok("the engine itself never calls the network",
    (engine.match(NETWORK) || []).length === 0, JSON.stringify(engine.match(NETWORK) || []));

  // Strip the counter block and nothing that touches a network may remain, so
  // a stray request can never be added elsewhere without this failing.
  const counter = html.match(/<script data-count="strata">[\s\S]*?<\/script>/);
  ok("the view counter is one self-contained block", !!counter);
  const withoutCounter = counter ? html.replace(counter[0], "") : html;
  ok("every network call on the page lives inside that one block",
    !(withoutCounter.match(NETWORK) || []).length,
    JSON.stringify(withoutCounter.match(NETWORK) || []));
  ok("and the FAQ discloses it rather than claiming zero requests",
    /a page was opened/i.test(html) && /Do Not Track/i.test(html));
  ok("file contents are hashed locally with the browser's own crypto",
    /crypto\.subtle\.digest\("SHA-256"/.test(engine));
}

group("the page is usable without a mouse or a screen");
{
  ok("the canvas is marked decorative", /<canvas id="map" aria-hidden="true">/.test(html));
  ok("a real table mirrors the map for keyboard and screen-reader users",
    /id="mapTable"/.test(html) && /works with a keyboard and a screen reader/i.test(html));
  ok("tabs are wired up as tabs", (html.match(/role="tab"/g) || []).length >= 6);
  ok("reduced motion turns off animation, transition and smooth scrolling",
    /prefers-reduced-motion: reduce\)\{[\s\S]*?scroll-behavior:auto[\s\S]*?animation:none!important[\s\S]*?transition:none!important/.test(html));
  ok("the file input has a label", /<label class="sr" for="dirInput"/.test(html));
}

console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail ? 1 : 0);
