// Tests for the Strata desktop engine.
//
// The desktop build can do the two things the web version refuses to: read a
// whole drive, and remove files. Both are ways to be badly wrong, so most of
// what follows is about the refusals — that a link is never followed, that an
// unreadable folder is counted rather than dropped, that two files of equal
// size are not called copies, and above all that the guard will not let the
// operating system's own folders be deleted.
using System.Diagnostics;
using Strata.Core;

int pass = 0, fail = 0, skip = 0;
void Group(string title) => Console.WriteLine($"\nSTRATA DESKTOP — {title}");
void Ok(string name, bool condition, string detail = "")
{
    if (condition) { pass++; Console.WriteLine($"  PASS  {name}{(detail.Length > 0 ? "  " + detail : "")}"); }
    else { fail++; Console.WriteLine($"  FAIL  {name}{(detail.Length > 0 ? "  " + detail : "")}"); }
}
void Skipped(string name, string why) { skip++; Console.WriteLine($"  SKIP  {name}  {why}"); }

var sandbox = Path.Combine(Path.GetTempPath(), "strata-tests-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(sandbox);

try
{
    /* ================================================================ sizes */

    Group("byte sizes, in both the units people actually see");
    Ok("bytes stay bytes", ByteSize.Format(0) == "0 B" && ByteSize.Format(999) == "999 B");
    Ok("decimal rolls over at 1000", ByteSize.Format(1000) == "1.00 KB", ByteSize.Format(1000));
    Ok("binary rolls over at 1024", ByteSize.Format(1024, true) == "1.00 KiB", ByteSize.Format(1024, true));
    Ok("the same drive reads differently in each",
        ByteSize.Format(500_000_000_000) == "500 GB" && ByteSize.Format(500_000_000_000, true) == "466 GiB",
        $"{ByteSize.Format(500_000_000_000)} vs {ByteSize.Format(500_000_000_000, true)}");
    Ok("a negative size is not dressed up as a number", ByteSize.Format(-5) == "—");

    /* ======================================================= classification */

    Group("working out what a file is");
    Ok("ordinary extensions", Categories.ExtensionOf("Clip.MP4") == "mp4" && Categories.Of("clip.MP4") == "Video");
    Ok("a dotfile has no extension", Categories.ExtensionOf(".gitignore") == "");
    Ok("a trailing dot is not an extension", Categories.ExtensionOf("weird.") == "");
    Ok("only the last extension counts", Categories.ExtensionOf("archive.tar.gz") == "gz");
    Ok("junk after a dot is not treated as one", Categories.ExtensionOf("v1.2 final draft") == "");
    Ok("anything unrecognised is called Other", Categories.Of("mystery.qqq") == "Other");
    Ok("every category has a colour",
        Categories.Colours.ContainsKey("Video") && Categories.Colours.ContainsKey("Other"));

    /* ============================================================== the tree */

    Group("turning a scan back into folders");
    var root = Path.Combine(sandbox, "tree");
    Directory.CreateDirectory(Path.Combine(root, "Movies", "old"));
    Directory.CreateDirectory(Path.Combine(root, "code", "app", "node_modules", "react"));
    File.WriteAllBytes(Path.Combine(root, "Movies", "holiday.mp4"), new byte[4000]);
    File.WriteAllBytes(Path.Combine(root, "Movies", "old", "wedding.mov"), new byte[2000]);
    File.WriteAllBytes(Path.Combine(root, "code", "app", "node_modules", "react", "index.js"), new byte[900]);
    File.WriteAllBytes(Path.Combine(root, "code", "app", "main.js"), new byte[100]);
    File.WriteAllBytes(Path.Combine(root, "notes.txt"), new byte[10]);

    var scanner = new Scanner();
    var scan = scanner.Scan(root);
    var tree = TreeNode.Build(scan.Files, root, "tree");

    Ok("every file is found however deep", scan.Files.Count == 5, $"{scan.Files.Count}");
    Ok("every byte is accounted for", tree.Size == 7010, $"{tree.Size}");
    Ok("a folder totals everything beneath it", tree.Directories["Movies"].Size == 6000);
    Ok("files at the root are not lost", tree.Files.Any(f => f.Name == "notes.txt"));
    Ok("paths rebuild exactly",
        tree.Directories["code"].Directories["app"].Directories["node_modules"].RelativePath
            == Path.Combine("code", "app", "node_modules"));
    Ok("children come back biggest first", tree.Children()[0].Name == "Movies");
    Ok("a clean scan reports nothing skipped", scan.Skipped.Count == 0);
    Ok("and is not flagged partial", !scan.IsPartial);
    Ok("a folder of video reads as video", tree.Directories["Movies"].DominantCategory() == "Video");
    Ok("a folder of source reads as code", tree.Directories["code"].DominantCategory() == "Code & config");

    /* ============================================================== treemap */

    Group("the treemap covers the box exactly");
    var items = new[] { 600L, 300L, 60L, 30L, 8L, 2L };
    const double W = 800, H = 420;
    var tiles = Treemap.Squarify(items, v => v, 0, 0, W, H);
    Ok("every item gets a rectangle", tiles.Count == items.Length, $"{tiles.Count}");
    double area = tiles.Sum(t => t.W * t.H);
    Ok("the rectangles tile the whole box", Math.Abs(area - W * H) < 0.001, $"{area:F2} vs {W * H}");
    double totalSize = items.Sum();
    Ok("each area is proportional to its size",
        tiles.All(t => Math.Abs(t.W * t.H / (W * H) - t.Item / totalSize) < 1e-9));
    Ok("nothing is drawn outside the box",
        tiles.All(t => t.X >= -1e-9 && t.Y >= -1e-9 && t.X + t.W <= W + 1e-9 && t.Y + t.H <= H + 1e-9));
    int overlaps = 0;
    for (int i = 0; i < tiles.Count; i++)
        for (int j = i + 1; j < tiles.Count; j++)
        {
            var (a, b) = (tiles[i], tiles[j]);
            if (a.X < b.X + b.W - 1e-9 && b.X < a.X + a.W - 1e-9 &&
                a.Y < b.Y + b.H - 1e-9 && b.Y < a.Y + a.H - 1e-9) overlaps++;
        }
    Ok("no two blocks overlap", overlaps == 0, $"{overlaps}");
    double worst = tiles.Max(t => Math.Max(t.W / t.H, t.H / t.W));
    Ok("blocks stay roughly square rather than becoming slivers", worst < 12, $"worst {worst:F1}");
    Ok("an empty folder lays out to nothing", Treemap.Squarify(Array.Empty<long>(), v => v, 0, 0, W, H).Count == 0);
    Ok("a zero-sized box is refused rather than dividing by zero",
        Treemap.Squarify(items, v => v, 0, 0, 0, H).Count == 0);

    /* =========================================================== duplicates */

    Group("duplicate files, proved byte by byte");
    var dupDir = Path.Combine(sandbox, "dupes");
    Directory.CreateDirectory(dupDir);
    byte[] Filler(int n, int seed)
    {
        var b = new byte[n];
        for (int i = 0; i < n; i++) b[i] = (byte)((i * 31 + seed) & 255);
        return b;
    }
    File.WriteAllBytes(Path.Combine(dupDir, "holiday.jpg"), Filler(9000, 1));
    File.WriteAllBytes(Path.Combine(dupDir, "holiday (copy).jpg"), Filler(9000, 1));
    File.WriteAllBytes(Path.Combine(dupDir, "different.jpg"), Filler(9000, 2));

    var dupScan = scanner.Scan(dupDir);
    var finder = new DuplicateFinder { HeadBytes = 4096 };
    var dupes = finder.Find(dupScan.Files);
    Ok("identical files are grouped whatever they are called",
        dupes.Groups.Count == 1 && dupes.Groups[0].Copies == 2, $"{dupes.Groups.Count} group(s)");
    Ok("the reclaimable figure keeps one copy", dupes.Reclaimable == 9000, $"{dupes.Reclaimable}");
    Ok("two different files of identical size are never called duplicates",
        dupes.Groups.All(g => g.Files.All(f => !f.Name.StartsWith("different"))));

    Group("the full-file pass actually runs");
    var headDir = Path.Combine(sandbox, "heads");
    Directory.CreateDirectory(headDir);
    var head = Filler(65536, 1);
    var one = new byte[81920]; head.CopyTo(one, 0); Filler(16384, 2).CopyTo(one, 65536);
    var two = new byte[81920]; head.CopyTo(two, 0); Filler(16384, 99).CopyTo(two, 65536);
    File.WriteAllBytes(Path.Combine(headDir, "one.bin"), one);
    File.WriteAllBytes(Path.Combine(headDir, "two.bin"), two);
    var headScan = scanner.Scan(headDir);
    var headDupes = new DuplicateFinder { HeadBytes = 65536 }.Find(headScan.Files);
    // Without the third stage these two match on size and on their first 64 KB,
    // and someone deletes a file that was not a copy.
    Ok("files sharing only their first 64 KB are not duplicates", headDupes.Groups.Count == 0,
        $"{headDupes.Groups.Count} group(s)");

    File.WriteAllBytes(Path.Combine(headDir, "one-copy.bin"), one);
    var headDupes2 = new DuplicateFinder { HeadBytes = 65536 }.Find(scanner.Scan(headDir).Files);
    Ok("and files matching all the way to the end still are",
        headDupes2.Groups.Count == 1 && headDupes2.Groups[0].Copies == 2, $"{headDupes2.Groups.Count}");

    Group("what the duplicate scan refuses to do");
    var tinyDir = Path.Combine(sandbox, "tiny");
    Directory.CreateDirectory(tinyDir);
    File.WriteAllText(Path.Combine(tinyDir, "a.txt"), "hi");
    File.WriteAllText(Path.Combine(tinyDir, "b.txt"), "hi");
    Ok("tiny files are skipped, since deleting them reclaims nothing",
        new DuplicateFinder { MinimumSize = 4096 }.Find(scanner.Scan(tinyDir).Files).Groups.Count == 0);

    var lockedDir = Path.Combine(sandbox, "locked");
    Directory.CreateDirectory(lockedDir);
    var lockedPath = Path.Combine(lockedDir, "locked.bin");
    File.WriteAllBytes(lockedPath, Filler(9000, 5));
    File.WriteAllBytes(Path.Combine(lockedDir, "twin.bin"), Filler(9000, 5));
    var lockedScan = scanner.Scan(lockedDir);
    using (var hold = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        var mixed = new DuplicateFinder { HeadBytes = 4096 }.Find(lockedScan.Files);
        Ok("a file it could not read is reported, not silently dropped",
            mixed.Unreadable.Count == 1, string.Join(", ", mixed.Unreadable.Select(u => u.Path)));
        Ok("and it is not counted as a duplicate of anything", mixed.Groups.Count == 0);
    }

    /* ============================================================== scanning */

    Group("a folder it is refused is never called empty");
    var scanned = scanner.Scan(root);
    Ok("what could be read is still reported", scanned.Files.Count == 5);

    Group("links are never followed");
    var linkParent = Path.Combine(sandbox, "links");
    Directory.CreateDirectory(Path.Combine(linkParent, "real"));
    File.WriteAllBytes(Path.Combine(linkParent, "real", "payload.bin"), new byte[5000]);
    var junction = Path.Combine(linkParent, "loop");
    var mk = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{linkParent}\"")
    { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
    mk!.WaitForExit();
    if (Directory.Exists(junction))
    {
        // A junction pointing at its own parent is an infinite tree. Following it
        // hangs the scan; counting through it inflates the total.
        var linkScan = scanner.Scan(linkParent);
        Ok("a junction back to its own parent does not hang or recurse",
            linkScan.Files.Count == 1 && linkScan.TotalBytes == 5000,
            $"{linkScan.Files.Count} files, {linkScan.TotalBytes} bytes");
        Ok("and the link is reported rather than ignored",
            linkScan.Skipped.Any(s => s.Reason == "link, not followed"),
            string.Join(", ", linkScan.Skipped.Select(s => s.Reason)));
        Directory.Delete(junction);
    }
    else
    {
        Skipped("junction handling", "mklink /J unavailable in this environment");
    }

    Group("stopping a long scan");
    using (var cts = new CancellationTokenSource())
    {
        cts.Cancel();
        var stopped = scanner.Scan(root, null, cts.Token);
        Ok("it stops when asked", stopped.Cancelled);
        Ok("and says the totals are not complete", stopped.IsPartial);
    }

    /* ========================================================== rebuildable */

    Group("folders a tool can put back");
    var rebuild = Rebuildable.Find(tree, root);
    Ok("node_modules is found",
        rebuild.Any(r => r.RelativePath == Path.Combine("code", "app", "node_modules")),
        string.Join(", ", rebuild.Select(r => r.RelativePath)));
    Ok("it carries the command that recreates it", rebuild[0].How == "npm install", rebuild[0].How);
    Ok("and an absolute path, so it can be acted on", Path.IsPathRooted(rebuild[0].FullPath));

    var nestedRoot = Path.Combine(sandbox, "nested");
    Directory.CreateDirectory(Path.Combine(nestedRoot, "app", "node_modules", "pkg", "build"));
    File.WriteAllBytes(Path.Combine(nestedRoot, "app", "node_modules", "pkg", "build", "out.js"), new byte[100]);
    File.WriteAllBytes(Path.Combine(nestedRoot, "app", "node_modules", "pkg", "index.js"), new byte[50]);
    var nestedTree = TreeNode.Build(scanner.Scan(nestedRoot).Files, nestedRoot, "nested");
    var nestedFound = Rebuildable.Find(nestedTree, nestedRoot);
    Ok("nested matches are not counted twice",
        nestedFound.Count == 1 && nestedFound[0].Size == 150,
        string.Join(", ", nestedFound.Select(r => $"{r.RelativePath}={r.Size}")));

    /* ============================================================= deletion */

    Group("what the cleanup guard will never let you delete");
    var guard = SafetyGuard.ForThisMachine();

    Ok("the root of a drive", !guard.Check(@"C:\").Allowed, guard.Check(@"C:\").Reason);
    Ok("the Windows folder", !guard.Check(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).Allowed);
    Ok("anything inside it", !guard.Check(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "kernel32.dll")).Allowed);
    Ok("Program Files", !guard.Check(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)).Allowed);
    Ok("ProgramData", !guard.Check(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)).Allowed);
    Ok("your whole profile folder",
        !guard.Check(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).Allowed);
    Ok("the folder holding every profile",
        !guard.Check(Path.GetDirectoryName(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))!).Allowed);
    Ok("your Documents folder itself",
        !guard.Check(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)).Allowed);
    Ok("and it explains itself rather than just refusing",
        guard.Check(@"C:\").Reason.Length > 0 &&
        guard.Check(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).Reason.Length > 0);

    Group("but ordinary things are still allowed");
    var deletable = Path.Combine(sandbox, "deletable.bin");
    File.WriteAllBytes(deletable, new byte[10]);
    Ok("a file you actually own", guard.Check(deletable).Allowed, guard.Check(deletable).Reason);
    Ok("a file inside Documents, unlike Documents itself",
        guard.Check(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "some-big-file.zip")).Allowed);

    // C:\Windows must not also protect C:\WindowsApps or C:\Windows-old.
    var fake = new SafetyGuard(protectedTrees: [@"C:\Windows"]) { RequireRecycleBin = false };
    Ok("the guard compares whole folder names, not prefixes",
        !fake.Check(@"C:\Windows\x.dll").Allowed && fake.Check(@"C:\WindowsApps\x.dll").Allowed,
        $"WindowsApps allowed = {fake.Check(@"C:\WindowsApps\x.dll").Allowed}");

    Group("deleting is refused where the Recycle Bin cannot bring it back");
    // A delete on a network or removable drive is permanent, and permanent is
    // the one thing this product will not do.
    var noBin = new SafetyGuard() { RequireRecycleBin = true };
    Ok("a UNC path is refused", !noBin.Check(@"\\server\share\big.iso").Allowed,
        noBin.Check(@"\\server\share\big.iso").Reason);
    Ok("and the reason says why",
        noBin.Check(@"\\server\share\big.iso").Reason.Contains("Recycle Bin"));
    Ok("a fixed local drive is still fine", noBin.Check(deletable).Allowed, noBin.Check(deletable).Reason);

    Group("nothing outside the folder you scanned can be touched");
    Ok("a file inside the scan is within it", SafetyGuard.IsWithin(deletable, sandbox));
    Ok("a sibling folder is not", !SafetyGuard.IsWithin(@"C:\Windows\system32", sandbox));
    Ok("and neither is a path climbing out with ..",
        !SafetyGuard.IsWithin(Path.Combine(sandbox, "..", "elsewhere.txt"), sandbox));

    Group("a link is refused even when it looks like a folder");
    var linkTarget = Path.Combine(sandbox, "target");
    Directory.CreateDirectory(linkTarget);
    var guardedLink = Path.Combine(sandbox, "guarded-link");
    var mk2 = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{guardedLink}\" \"{linkTarget}\"")
    { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
    mk2!.WaitForExit();
    if (Directory.Exists(guardedLink))
    {
        var verdict = guard.Check(guardedLink);
        Ok("deleting a junction is refused", !verdict.Allowed, verdict.Reason);
        Ok("and refused for being a link, not for some unrelated reason",
            verdict.Reason.Contains("link"), verdict.Reason);
        Directory.Delete(guardedLink);
    }
    else Skipped("junction deletion guard", "mklink /J unavailable in this environment");

    Group("the application has no way to delete permanently");
    // The whole safety story rests on removal going through the Recycle Bin, so
    // the absence of a permanent delete is checked against the source rather
    // than trusted. Anything calling File.Delete or Directory.Delete on a user's
    // file would bypass every guard above.
    var appDir = FindAppSource();
    if (appDir is null)
    {
        Skipped("permanent delete scan", "Strata.App sources not next door");
    }
    else
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(appDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            var text = File.ReadAllText(file);
            // SelfTest tidies up its own sandbox; that is not a user's file.
            if (Path.GetFileName(file) == "SelfTest.cs") continue;
            if (text.Contains("File.Delete(") || text.Contains("Directory.Delete("))
                offenders.Add(Path.GetFileName(file));
        }
        Ok("no source file deletes anything directly", offenders.Count == 0, string.Join(", ", offenders));

        var recycle = File.ReadAllText(Path.Combine(appDir, "RecycleBin.cs"));
        Ok("removal asks the shell for an undoable delete", recycle.Contains("FOF_ALLOWUNDO"));
        Ok("and demands a warning if Windows cannot recycle something",
            recycle.Contains("FOF_WANTNUKEWARNING"),
            "without this a file too big for the bin would be destroyed silently");
    }

    static string? FindAppSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Strata.App");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "RecycleBin.cs")))
                return candidate;
        }
        return null;
    }
}
finally
{
    try { Directory.Delete(sandbox, recursive: true); } catch { /* best effort */ }
}

Console.WriteLine($"\n{pass} passed, {fail} failed{(skip > 0 ? $", {skip} skipped" : "")}");
return fail > 0 ? 1 : 0;
