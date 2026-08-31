namespace Strata.Core;

public sealed record DeleteCheck(bool Allowed, string Reason)
{
    public static readonly DeleteCheck Ok = new(true, "");
    public static DeleteCheck No(string reason) => new(false, reason);
}

/// <summary>
/// Decides what Strata is allowed to remove. It is deliberately blunt: the
/// operating system's own folders, the roots of a drive and of your profile,
/// and links are all refused outright, whatever the size says. Reclaiming a
/// few gigabytes is never worth a machine that will not boot.
/// </summary>
public sealed class SafetyGuard
{
    private readonly string[] _protectedTrees;   // the folder and everything inside it
    private readonly string[] _protectedExact;   // the folder itself, contents are fine

    /// <summary>
    /// Removable and network drives usually have no Recycle Bin, so a delete
    /// there is permanent. Strata will not do permanent.
    /// </summary>
    public bool RequireRecycleBin { get; init; } = true;

    public SafetyGuard(IEnumerable<string>? protectedTrees = null, IEnumerable<string>? protectedExact = null)
    {
        _protectedTrees = [.. (protectedTrees ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Select(Trim)];
        _protectedExact = [.. (protectedExact ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Select(Trim)];
    }

    public static SafetyGuard ForThisMachine()
    {
        string Folder(Environment.SpecialFolder f) => Environment.GetFolderPath(f);

        var trees = new List<string>
        {
            Folder(Environment.SpecialFolder.Windows),
            Folder(Environment.SpecialFolder.System),
            Folder(Environment.SpecialFolder.SystemX86),
            Folder(Environment.SpecialFolder.ProgramFiles),
            Folder(Environment.SpecialFolder.ProgramFilesX86),
            Folder(Environment.SpecialFolder.CommonApplicationData),
        };

        var profile = Folder(Environment.SpecialFolder.UserProfile);
        var exact = new List<string>
        {
            profile,
            Folder(Environment.SpecialFolder.Desktop),
            Folder(Environment.SpecialFolder.MyDocuments),
            Folder(Environment.SpecialFolder.MyMusic),
            Folder(Environment.SpecialFolder.MyPictures),
            Folder(Environment.SpecialFolder.MyVideos),
            Folder(Environment.SpecialFolder.ApplicationData),
            Folder(Environment.SpecialFolder.LocalApplicationData),
        };
        if (!string.IsNullOrEmpty(profile))
        {
            // The folder holding every user profile is protected as a folder, not
            // as a tree: almost everything worth cleaning up lives inside it.
            var users = Path.GetDirectoryName(profile);
            if (!string.IsNullOrEmpty(users)) exact.Add(users);
            exact.Add(Path.Combine(profile, "Downloads"));
        }

        return new SafetyGuard(trees, exact);
    }

    public DeleteCheck Check(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return DeleteCheck.No("no path");

        string full;
        try { full = Trim(Path.GetFullPath(path)); }
        catch (Exception ex) { return DeleteCheck.No(Scanner.Describe(ex)); }

        if (IsDriveRoot(full))
            return DeleteCheck.No("this is the root of a drive");

        foreach (var tree in _protectedTrees)
            if (Same(full, tree) || IsUnder(full, tree))
                return DeleteCheck.No("this belongs to the operating system");

        foreach (var one in _protectedExact)
            if (Same(full, one))
                return DeleteCheck.No("this is a folder your account needs");

        if (RequireRecycleBin && !RecycleBinProtects(full))
            return DeleteCheck.No("the Recycle Bin does not protect this drive, so deleting would be permanent");

        try
        {
            var info = File.Exists(full) ? new FileInfo(full) : (FileSystemInfo)new DirectoryInfo(full);
            if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0)
                return DeleteCheck.No("this is a link to somewhere else");
        }
        catch (Exception ex)
        {
            return DeleteCheck.No(Scanner.Describe(ex));
        }

        return DeleteCheck.Ok;
    }

    /// <summary>Nothing outside the folder the user actually scanned may be touched.</summary>
    public static bool IsWithin(string path, string root)
    {
        try
        {
            var full = Trim(Path.GetFullPath(path));
            var top = Trim(Path.GetFullPath(root));
            return Same(full, top) || IsUnder(full, top);
        }
        catch { return false; }
    }

    private static string Trim(string p) =>
        p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool Same(string a, string b) =>
        a.Equals(b, StringComparison.OrdinalIgnoreCase);

    // Compared a segment at a time, or C:\Windows would also protect C:\WindowsApps.
    private static bool IsUnder(string path, string root) =>
        root.Length > 0 &&
        path.Length > root.Length &&
        path[root.Length] == Path.DirectorySeparatorChar &&
        path.AsSpan(0, root.Length).Equals(root.AsSpan(), StringComparison.OrdinalIgnoreCase);

    private static bool IsDriveRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(root) && Same(Trim(root), path);
    }

    private static bool RecycleBinProtects(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return false;
            return new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch { return false; }
    }
}
