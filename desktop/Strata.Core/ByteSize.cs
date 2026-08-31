namespace Strata.Core;

/// <summary>Sizes the way each operating system reports them.</summary>
public static class ByteSize
{
    private static readonly string[] Decimal = ["B", "KB", "MB", "GB", "TB", "PB"];
    private static readonly string[] Binary = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];

    public static string Format(long bytes, bool binary = false)
    {
        if (bytes < 0) return "—";
        double base_ = binary ? 1024d : 1000d;
        string[] units = binary ? Binary : Decimal;
        if (bytes < base_) return $"{bytes} B";

        double value = bytes;
        int i = 0;
        while (value >= base_ && i < units.Length - 1) { value /= base_; i++; }
        string number = value >= 100 ? value.ToString("0") : value >= 10 ? value.ToString("0.0") : value.ToString("0.00");
        return $"{number} {units[i]}";
    }
}
