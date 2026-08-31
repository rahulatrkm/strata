using System.Collections.Concurrent;

namespace Strata.Core;

public sealed record FileEntry(string FullPath, string Name, long Length, DateTime LastWriteUtc);

public sealed record SkippedItem(string Path, string Reason);

public sealed class ScanResult
{
    public required IReadOnlyList<FileEntry> Files { get; init; }
    public required IReadOnlyList<SkippedItem> Skipped { get; init; }
    public required string Root { get; init; }
    public long TotalBytes { get; init; }
    public int DirectoryCount { get; init; }
    public bool Cancelled { get; init; }

    /// <summary>True when something was unreadable, so totals are a floor rather than the truth.</summary>
    public bool IsPartial => Skipped.Count > 0 || Cancelled;
}

public readonly record struct ScanProgress(int Files, int Directories, long Bytes, string Current);

/// <summary>
/// Walks a folder tree. Two rules it never breaks: anything unreadable is
/// recorded rather than dropped, and reparse points are never followed.
/// </summary>
public sealed class Scanner
{
    private readonly EnumerationOptions _options = new()
    {
        // Errors must surface so they can be counted; skipping them quietly is
        // how a disk tool ends up under-reporting a drive.
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.None,
        ReturnSpecialDirectories = false,
    };

    public int MaxDegreeOfParallelism { get; init; } = Math.Max(2, Environment.ProcessorCount);

    public ScanResult Scan(string root, IProgress<ScanProgress>? progress = null, CancellationToken token = default)
    {
        var files = new ConcurrentBag<FileEntry>();
        var skipped = new ConcurrentBag<SkippedItem>();
        var visited = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        long totalBytes = 0;
        int fileCount = 0, dirCount = 0;
        bool cancelled = false;

        var level = new List<string> { root };
        visited.TryAdd(Normalise(root), 0);

        while (level.Count > 0)
        {
            if (token.IsCancellationRequested) { cancelled = true; break; }

            var next = new ConcurrentBag<string>();
            var options = new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism };

            try
            {
                Parallel.ForEach(level, options, dir =>
                {
                    if (token.IsCancellationRequested) return;
                    Interlocked.Increment(ref dirCount);

                    foreach (var entry in SafeEnumerate(dir, skipped))
                    {
                        if (token.IsCancellationRequested) return;
                        try
                        {
                            var info = entry;
                            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                // A junction or symlink points at bytes counted elsewhere.
                                // Following it double-counts at best and loops forever at worst.
                                skipped.Add(new SkippedItem(info.FullName, "link, not followed"));
                                continue;
                            }

                            if (info is DirectoryInfo d)
                            {
                                if (visited.TryAdd(Normalise(d.FullName), 0)) next.Add(d.FullName);
                            }
                            else if (info is FileInfo f)
                            {
                                files.Add(new FileEntry(f.FullName, f.Name, f.Length, f.LastWriteTimeUtc));
                                Interlocked.Increment(ref fileCount);
                                Interlocked.Add(ref totalBytes, f.Length);
                            }
                        }
                        catch (Exception ex)
                        {
                            skipped.Add(new SkippedItem(entry.FullName, Describe(ex)));
                        }
                    }
                });
            }
            catch (OperationCanceledException) { cancelled = true; break; }

            progress?.Report(new ScanProgress(fileCount, dirCount, Interlocked.Read(ref totalBytes),
                level.Count > 0 ? level[0] : root));
            level = [.. next];
        }

        if (token.IsCancellationRequested) cancelled = true;

        return new ScanResult
        {
            Files = [.. files],
            Skipped = [.. skipped],
            Root = root,
            TotalBytes = totalBytes,
            DirectoryCount = dirCount,
            Cancelled = cancelled,
        };
    }

    private IEnumerable<FileSystemInfo> SafeEnumerate(string dir, ConcurrentBag<SkippedItem> skipped)
    {
        IEnumerator<FileSystemInfo> it;
        try
        {
            it = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", _options).GetEnumerator();
        }
        catch (Exception ex)
        {
            skipped.Add(new SkippedItem(dir, Describe(ex)));
            yield break;
        }

        while (true)
        {
            FileSystemInfo current;
            try
            {
                if (!it.MoveNext()) break;
                current = it.Current;
            }
            catch (Exception ex)
            {
                // One unreadable entry must not abandon the rest of the folder.
                skipped.Add(new SkippedItem(dir, Describe(ex)));
                break;
            }
            yield return current;
        }
        it.Dispose();
    }

    private static string Normalise(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "permission denied",
        DirectoryNotFoundException => "disappeared while reading",
        FileNotFoundException => "disappeared while reading",
        PathTooLongException => "path too long to read",
        IOException => "in use or unreadable",
        _ => "could not be read",
    };
}
