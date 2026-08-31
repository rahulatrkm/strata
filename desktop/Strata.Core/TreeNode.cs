namespace Strata.Core;

public sealed class TreeNode
{
    public required string Name { get; init; }
    public TreeNode? Parent { get; set; }
    public Dictionary<string, TreeNode> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<FileEntry> Files { get; } = [];

    public long Size { get; private set; }
    public int Count { get; private set; }
    private string? _dominant;

    public bool IsRoot => Parent is null;

    public string RelativePath
    {
        get
        {
            var parts = new List<string>();
            for (var n = this; n is { Parent: not null }; n = n.Parent) parts.Add(n.Name);
            parts.Reverse();
            return string.Join(Path.DirectorySeparatorChar, parts);
        }
    }

    public string FullPath(string root) => IsRoot ? root : Path.Combine(root, RelativePath);

    public static TreeNode Build(IEnumerable<FileEntry> files, string root, string rootName)
    {
        var tree = new TreeNode { Name = rootName };
        int prefix = root.TrimEnd(Path.DirectorySeparatorChar).Length;

        foreach (var file in files)
        {
            var relative = file.FullPath.Length > prefix
                ? file.FullPath[prefix..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : file.Name;

            var segments = relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) continue;

            var dir = tree;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (!dir.Directories.TryGetValue(segments[i], out var child))
                {
                    child = new TreeNode { Name = segments[i], Parent = dir };
                    dir.Directories[segments[i]] = child;
                }
                dir = child;
            }
            dir.Files.Add(file);
        }

        Aggregate(tree);
        return tree;
    }

    private static void Aggregate(TreeNode node)
    {
        long size = 0;
        int count = 0;
        foreach (var f in node.Files) { size += f.Length; count++; }
        foreach (var child in node.Directories.Values)
        {
            Aggregate(child);
            size += child.Size;
            count += child.Count;
        }
        node.Size = size;
        node.Count = count;
    }

    public IEnumerable<FileEntry> AllFiles()
    {
        foreach (var f in Files) yield return f;
        foreach (var d in Directories.Values)
            foreach (var f in d.AllFiles()) yield return f;
    }

    /// <summary>What kind of file fills this folder, so a block can be coloured by its contents.</summary>
    public string DominantCategory()
    {
        if (_dominant is not null) return _dominant;
        var totals = new Dictionary<string, long>();
        foreach (var f in AllFiles())
        {
            var cat = Categories.Of(f.Name);
            totals[cat] = totals.GetValueOrDefault(cat) + f.Length;
        }
        string best = "Other";
        long bestSize = -1;
        foreach (var (cat, size) in totals)
            if (size > bestSize) { best = cat; bestSize = size; }
        return _dominant = best;
    }

    /// <summary>Children as one ranked list of folders and files.</summary>
    public List<TreeItem> Children()
    {
        var items = new List<TreeItem>(Directories.Count + Files.Count);
        foreach (var d in Directories.Values)
            items.Add(new TreeItem(d.Name, d.Size, true, d.Count, d, null));
        foreach (var f in Files)
            items.Add(new TreeItem(f.Name, f.Length, false, 1, null, f));
        items.Sort((a, b) => b.Size.CompareTo(a.Size));
        return items;
    }
}

public sealed record TreeItem(string Name, long Size, bool IsDirectory, int Count, TreeNode? Node, FileEntry? File)
{
    public string Category => IsDirectory ? Node!.DominantCategory() : Categories.Of(Name);
}
