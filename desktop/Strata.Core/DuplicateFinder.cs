using System.Security.Cryptography;

namespace Strata.Core;

public sealed record DuplicateGroup(long Size, IReadOnlyList<FileEntry> Files)
{
    public int Copies => Files.Count;
    /// <summary>What you would get back keeping one copy.</summary>
    public long Wasted => Size * (Copies - 1);
}

public sealed record DuplicateResult(IReadOnlyList<DuplicateGroup> Groups, IReadOnlyList<SkippedItem> Unreadable)
{
    public long Reclaimable => Groups.Sum(g => g.Wasted);
}

public readonly record struct HashProgress(int Done, int Total);

/// <summary>
/// Finds files with identical contents. Three stages, cheapest first: exact
/// size, a hash of the first 64 KB, then the whole file. Two files are only
/// ever called copies when every byte agrees.
/// </summary>
public sealed class DuplicateFinder
{
    public int HeadBytes { get; init; } = 64 * 1024;
    public long MinimumSize { get; init; } = 4096;

    public DuplicateResult Find(IEnumerable<FileEntry> files,
        IProgress<HashProgress>? progress = null, CancellationToken token = default)
    {
        var unreadable = new List<SkippedItem>();

        var candidates = files
            .Where(f => f.Length >= MinimumSize)
            .GroupBy(f => f.Length)
            .Where(g => g.Count() > 1)
            .Select(g => (IReadOnlyList<FileEntry>)[.. g])
            .ToList();

        var byHead = HashStage(candidates, HeadBytes, unreadable, progress, token);

        var settled = new List<IReadOnlyList<FileEntry>>();
        var needFull = new List<IReadOnlyList<FileEntry>>();
        foreach (var group in byHead)
            (group[0].Length <= HeadBytes ? settled : needFull).Add(group);

        var byFull = needFull.Count > 0
            ? HashStage(needFull, null, unreadable, progress, token)
            : [];

        var groups = settled.Concat(byFull)
            .Select(g => new DuplicateGroup(g[0].Length, g))
            .OrderByDescending(g => g.Wasted)
            .ToList();

        return new DuplicateResult(groups, unreadable);
    }

    private List<IReadOnlyList<FileEntry>> HashStage(List<IReadOnlyList<FileEntry>> groups, int? limit,
        List<SkippedItem> unreadable, IProgress<HashProgress>? progress, CancellationToken token)
    {
        var result = new List<IReadOnlyList<FileEntry>>();
        int total = groups.Sum(g => g.Count), done = 0;

        foreach (var group in groups)
        {
            if (token.IsCancellationRequested) return result;
            var buckets = new Dictionary<string, List<FileEntry>>();
            foreach (var file in group)
            {
                if (token.IsCancellationRequested) return result;
                try
                {
                    string hash = Hash(file.FullPath, limit);
                    if (!buckets.TryGetValue(hash, out var list)) buckets[hash] = list = [];
                    list.Add(file);
                }
                catch (Exception ex)
                {
                    unreadable.Add(new SkippedItem(file.FullPath, Scanner.Describe(ex)));
                }
                progress?.Report(new HashProgress(++done, total));
            }
            foreach (var list in buckets.Values)
                if (list.Count > 1) result.Add(list);
        }
        return result;
    }

    private static string Hash(string path, int? limit)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 1 << 16, FileOptions.SequentialScan);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = new byte[1 << 16];
        long remaining = limit ?? long.MaxValue;
        while (remaining > 0)
        {
            int want = (int)Math.Min(buffer.Length, remaining);
            int read = stream.Read(buffer, 0, want);
            if (read <= 0) break;
            sha.AppendData(buffer, 0, read);
            remaining -= read;
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }
}
