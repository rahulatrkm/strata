using System.IO;
using System.Runtime.InteropServices;

namespace Strata.App;

public sealed record RemovalOutcome(int Moved, long Bytes, IReadOnlyList<string> Failures);

/// <summary>
/// Removal, and only ever to the Recycle Bin.
///
/// <see cref="System.IO.File.Delete"/> is deliberately not used anywhere in this
/// application. The shell is asked with FOF_ALLOWUNDO so everything lands
/// somewhere it can be dragged back out of, and with FOF_WANTNUKEWARNING so that
/// if Windows cannot recycle an item it must say so rather than destroy it
/// quietly.
/// </summary>
public static class RecycleBin
{
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT fileOp);

    public static RemovalOutcome Send(IReadOnlyList<string> paths, IntPtr owner)
    {
        var failures = new List<string>();
        if (paths.Count == 0) return new RemovalOutcome(0, 0, failures);

        long bytes = 0;
        foreach (var p in paths)
        {
            try
            {
                if (File.Exists(p)) bytes += new FileInfo(p).Length;
                else if (Directory.Exists(p))
                    bytes += new DirectoryInfo(p).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch { /* the size is only for the report */ }
        }

        // A double-null-terminated list is what the shell expects.
        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = FO_DELETE,
            pFrom = string.Join('\0', paths) + "\0\0",
            pTo = null,
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING | FOF_NOERRORUI | FOF_SILENT,
        };

        int result = SHFileOperation(ref op);
        if (result != 0)
        {
            failures.Add($"Windows refused the removal (code {result}).");
            return new RemovalOutcome(0, 0, failures);
        }
        if (op.fAnyOperationsAborted)
        {
            failures.Add("The removal was cancelled part-way through.");
        }

        int moved = 0;
        foreach (var p in paths)
            if (!File.Exists(p) && !Directory.Exists(p)) moved++;
            else failures.Add(Path.GetFileName(p) + " is still there.");

        return new RemovalOutcome(moved, bytes, failures);
    }
}
