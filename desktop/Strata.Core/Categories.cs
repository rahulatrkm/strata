namespace Strata.Core;

public static class Categories
{
    private static readonly Dictionary<string, string> Map = Build();

    public static readonly Dictionary<string, string> Colours = new()
    {
        ["Video"] = "#e11d48",
        ["Photos & images"] = "#f59e0b",
        ["Audio"] = "#8b5cf6",
        ["Archives"] = "#0ea5e9",
        ["Disk images"] = "#6366f1",
        ["Documents"] = "#0f766e",
        ["Code & config"] = "#16a34a",
        ["Design"] = "#db2777",
        ["Databases"] = "#0891b2",
        ["Fonts"] = "#7c3aed",
        ["Apps & binaries"] = "#475569",
        ["Backups & temp"] = "#a16207",
        ["Other"] = "#94a3b8",
    };

    private static Dictionary<string, string> Build()
    {
        var source = new Dictionary<string, string>
        {
            ["Video"] = "mp4 mov avi mkv wmv flv webm m4v mpg mpeg 3gp ts m2ts vob ogv mts",
            ["Photos & images"] = "jpg jpeg png gif bmp tiff tif heic heif webp avif raw cr2 cr3 nef arw dng orf rw2 svg ico",
            ["Audio"] = "mp3 wav flac aac m4a ogg wma aiff aif alac opus mid midi",
            ["Archives"] = "zip rar 7z tar gz bz2 xz tgz zst lz4 cab",
            ["Disk images"] = "iso dmg vmdk vdi qcow2 vhd vhdx img sparsebundle",
            ["Documents"] = "pdf doc docx xls xlsx ppt pptx odt ods odp txt rtf md epub mobi pages numbers key csv tsv",
            ["Code & config"] = "js mjs cjs ts tsx jsx py java c h cpp hpp cc cs go rs rb php swift kt kts scala sh bash ps1 sql json xml yml yaml toml ini html htm css scss sass less vue svelte lua r pl",
            ["Design"] = "psd ai sketch fig xd indd afphoto afdesign blend obj fbx stl 3ds dwg",
            ["Databases"] = "db sqlite sqlite3 mdb accdb realm dat",
            ["Fonts"] = "ttf otf woff woff2 eot",
            ["Apps & binaries"] = "exe msi app deb rpm appimage bin dll so dylib jar war apk pkg pdb lib obj",
            ["Backups & temp"] = "bak backup old orig tmp temp swp part crdownload download log etl",
        };

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, extensions) in source)
            foreach (var ext in extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                map[ext] = category;
        return map;
    }

    /// <summary>Lower-case extension without the dot. A dotfile has none.</summary>
    public static string ExtensionOf(string name)
    {
        int dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1) return "";
        var ext = name[(dot + 1)..].ToLowerInvariant();
        foreach (var c in ext)
            if (!char.IsAsciiLetterOrDigit(c)) return "";
        return ext.Length is > 0 and <= 12 ? ext : "";
    }

    public static string Of(string name) =>
        Map.TryGetValue(ExtensionOf(name), out var category) ? category : "Other";
}
