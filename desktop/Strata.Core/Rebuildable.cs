namespace Strata.Core;

public sealed record RebuildableFolder(string RelativePath, string FullPath, long Size, int Count, string How, string Confidence);

/// <summary>
/// Folders a tool can recreate from what is already on disk. This is a match on
/// the folder's name, not proof that anything can rebuild — every result says
/// what would put it back so a person can decide.
/// </summary>
public static class Rebuildable
{
    private static readonly Dictionary<string, (string How, string Confidence)> Known =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["node_modules"] = ("npm install", "high"),
            ["__pycache__"] = ("Python, automatically", "high"),
            [".venv"] = ("recreate the virtualenv", "high"),
            ["venv"] = ("recreate the virtualenv", "high"),
            [".gradle"] = ("Gradle, on next build", "high"),
            ["DerivedData"] = ("Xcode, on next build", "high"),
            [".terraform"] = ("terraform init", "high"),
            [".next"] = ("next build", "high"),
            [".nuxt"] = ("nuxt build", "high"),
            [".parcel-cache"] = ("Parcel, on next build", "high"),
            [".pytest_cache"] = ("pytest", "high"),
            [".mypy_cache"] = ("mypy", "high"),
            ["Pods"] = ("pod install", "high"),
            ["packages"] = ("the package manager", "medium"),
            ["target"] = ("cargo build / mvn package", "medium"),
            ["build"] = ("your build tool", "medium"),
            ["dist"] = ("your build tool", "medium"),
            ["out"] = ("your build tool", "medium"),
            ["vendor"] = ("the package manager", "medium"),
        };

    public static List<RebuildableFolder> Find(TreeNode root, string rootPath)
    {
        var found = new List<RebuildableFolder>();
        Walk(root);
        found.Sort((a, b) => b.Size.CompareTo(a.Size));
        return found;

        void Walk(TreeNode node)
        {
            if (!node.IsRoot && Known.TryGetValue(node.Name, out var match))
            {
                found.Add(new RebuildableFolder(node.RelativePath, node.FullPath(rootPath),
                    node.Size, node.Count, match.How, match.Confidence));
                return; // a match inside a match is already counted
            }
            foreach (var child in node.Directories.Values) Walk(child);
        }
    }
}
