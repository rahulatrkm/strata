using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using Strata.Core;

namespace Strata.App;

public sealed class RowItem : INotifyPropertyChanged
{
    private bool _selected;

    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public long Size { get; init; }
    public required string SizeText { get; set; }
    public string Extra { get; set; } = "";
    public bool Selectable { get; init; } = true;
    public bool IsDirectory { get; init; }
    public TreeNode? Node { get; init; }

    public bool Selected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; Raise(); SelectionChanged?.Invoke(); } }
    }

    public static event Action? SelectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class MainWindow : Window
{
    private readonly ObservableCollection<RowItem> _map = [];
    private readonly ObservableCollection<RowItem> _big = [];
    private readonly ObservableCollection<RowItem> _dupes = [];
    private readonly ObservableCollection<RowItem> _rebuild = [];
    private readonly ObservableCollection<RowItem> _types = [];

    private string _root = "";
    private ScanResult? _scan;
    private TreeNode? _tree;
    private TreeNode? _view;
    private CancellationTokenSource? _cts;
    private bool _binary;

    public MainWindow()
    {
        InitializeComponent();
        ListMap.ItemsSource = _map;
        ListBig.ItemsSource = _big;
        ListDupes.ItemsSource = _dupes;
        ListRebuild.ItemsSource = _rebuild;
        ListTypes.ItemsSource = _types;

        Map.Activated += item => { if (item.IsDirectory && item.Node is not null) ShowFolder(item.Node); };
        Map.Hovered += item => Map.ToolTip = item is null ? null
            : $"{item.Name}\n{ByteSize.Format(item.Size, _binary)}" + (item.IsDirectory ? $"\n{item.Count:N0} files" : "");

        RowItem.SelectionChanged += UpdateSelection;
        Closed += (_, _) => RowItem.SelectionChanged -= UpdateSelection;

        LoadDrives();
    }

    private string Fmt(long bytes) => ByteSize.Format(bytes, _binary);

    private void LoadDrives()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                CmbDrives.Items.Add(new ComboBoxItem
                {
                    Content = $"{drive.Name}  {ByteSize.Format(drive.TotalSize - drive.AvailableFreeSpace)} used of {ByteSize.Format(drive.TotalSize)}",
                    Tag = drive.RootDirectory.FullName,
                });
            }
            catch { /* a drive that vanished mid-enumeration is not worth a crash */ }
        }
        if (CmbDrives.Items.Count > 0) CmbDrives.SelectedIndex = 0;
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder to map", Multiselect = false };
        if (dialog.ShowDialog(this) == true)
        {
            _root = dialog.FolderName;
            TxtPath.Text = _root;
        }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_root) && CmbDrives.SelectedItem is ComboBoxItem item)
            _root = (string)item.Tag;
        if (string.IsNullOrEmpty(_root)) { TxtPath.Text = "Choose a folder or a drive first."; return; }
        if (!Directory.Exists(_root)) { TxtPath.Text = $"{_root} is not there any more."; return; }

        _cts = new CancellationTokenSource();
        BtnScan.IsEnabled = false;
        BtnStop.IsEnabled = true;
        BtnDupes.IsEnabled = false;
        BtnExport.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        TxtWarning.Visibility = Visibility.Collapsed;
        ClearAll();

        var progress = new Progress<ScanProgress>(p =>
            TxtPath.Text = $"{_root}  —  {p.Files:N0} files, {p.Directories:N0} folders, {Fmt(p.Bytes)} so far…");

        var root = _root;
        var token = _cts.Token;
        try
        {
            var scan = await Task.Run(() => new Scanner().Scan(root, progress, token), token);
            _scan = scan;
            _tree = TreeNode.Build(scan.Files, root, new DirectoryInfo(root).Name is { Length: > 0 } n ? n : root);
            ShowResults();
        }
        catch (OperationCanceledException)
        {
            TxtPath.Text = $"{root} — stopped.";
        }
        catch (Exception ex)
        {
            TxtPath.Text = $"That folder could not be read: {Scanner.Describe(ex)}.";
        }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
            BtnScan.IsEnabled = true;
            BtnStop.IsEnabled = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    /// <summary>Fills the window from a real scan without the picker, for --selftest.</summary>
    internal void LoadForSelfTest(string root)
    {
        _root = root;
        _scan = new Scanner().Scan(root);
        _tree = TreeNode.Build(_scan.Files, root, new DirectoryInfo(root).Name);
        ShowResults();
    }

    private void ClearAll()
    {
        _map.Clear(); _big.Clear(); _dupes.Clear(); _rebuild.Clear(); _types.Clear();
        Map.SetItems([]);
        Crumbs.Children.Clear();
        UpdateSelection();
    }

    private void ShowResults()
    {
        if (_scan is null || _tree is null) return;

        StatTotal.Text = Fmt(_tree.Size);
        StatFiles.Text = $"{_scan.Files.Count:N0}";
        StatFolders.Text = $"{_scan.DirectoryCount:N0}";
        StatSkipped.Text = $"{_scan.Skipped.Count:N0}";
        TxtPath.Text = _root;

        if (_scan.IsPartial)
        {
            var first = _scan.Skipped.Take(3).Select(s => $"{s.Path} ({s.Reason})");
            TxtWarning.Text = _scan.Cancelled
                ? "You stopped the scan, so these totals cover only what had been read."
                : $"{_scan.Skipped.Count:N0} item(s) could not be read, so every total here is a floor rather than the whole truth. For example: {string.Join("; ", first)}";
            TxtWarning.Visibility = Visibility.Visible;
        }

        ShowFolder(_tree);

        foreach (var f in _tree.AllFiles().OrderByDescending(f => f.Length).Take(300))
            _big.Add(new RowItem
            {
                Name = Rel(f.FullPath), FullPath = f.FullPath, Size = f.Length,
                SizeText = Fmt(f.Length), Extra = f.LastWriteUtc.ToLocalTime().ToShortDateString(),
            });

        foreach (var r in Rebuildable.Find(_tree, _root))
            _rebuild.Add(new RowItem
            {
                Name = r.RelativePath, FullPath = r.FullPath, Size = r.Size, IsDirectory = true,
                SizeText = Fmt(r.Size), Extra = $"{r.How} ({r.Confidence})",
            });

        long total = Math.Max(1, _tree.Size);
        var byType = _tree.AllFiles()
            .GroupBy(f => Categories.Of(f.Name))
            .Select(g => (Kind: g.Key, Size: g.Sum(f => f.Length), Count: g.Count()))
            .OrderByDescending(t => t.Size);
        foreach (var t in byType)
            _types.Add(new RowItem
            {
                Name = t.Kind, FullPath = "", Size = t.Size, Selectable = false,
                SizeText = Fmt(t.Size), Extra = $"{t.Size * 100.0 / total:F1}%  ·  {t.Count:N0} files",
            });

        BtnDupes.IsEnabled = true;
        BtnExport.IsEnabled = true;
    }

    private string Rel(string path) =>
        path.StartsWith(_root, StringComparison.OrdinalIgnoreCase) && path.Length > _root.Length
            ? path[_root.Length..].TrimStart(Path.DirectorySeparatorChar)
            : path;

    private void ShowFolder(TreeNode node)
    {
        _view = node;
        var items = node.Children();
        Map.Binary = _binary;
        Map.SetItems(items);

        _map.Clear();
        long total = Math.Max(1, node.Size);
        foreach (var i in items.Take(400))
            _map.Add(new RowItem
            {
                Name = i.Name + (i.IsDirectory ? "\\" : ""), FullPath = i.Node?.FullPath(_root) ?? "",
                Size = i.Size, SizeText = Fmt(i.Size), Extra = $"{i.Size * 100.0 / total:F0}%",
                IsDirectory = i.IsDirectory, Node = i.Node, Selectable = false,
            });

        Crumbs.Children.Clear();
        var chain = new List<TreeNode>();
        for (var n = node; n is not null; n = n.Parent) chain.Insert(0, n);
        foreach (var n in chain)
        {
            if (Crumbs.Children.Count > 0)
                Crumbs.Children.Add(new TextBlock { Text = "›", Margin = new Thickness(4, 0, 4, 0), Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center });
            var target = n;
            var button = new Button { Content = n.Name, Padding = new Thickness(8, 4, 8, 4), FontSize = 12 };
            button.Click += (_, _) => ShowFolder(target);
            Crumbs.Children.Add(button);
        }
    }

    private void ListMap_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ListMap.SelectedItem is RowItem { IsDirectory: true, Node: not null } row) ShowFolder(row.Node);
    }

    private async void FindDupes_Click(object sender, RoutedEventArgs e)
    {
        if (_scan is null) return;
        BtnDupes.IsEnabled = false;
        TxtDupes.Text = "Comparing…";
        _dupes.Clear();

        var files = _scan.Files;
        var result = await Task.Run(() => new DuplicateFinder().Find(files));

        int group = 0;
        foreach (var g in result.Groups.Take(400))
        {
            group++;
            bool first = true;
            foreach (var f in g.Files)
            {
                _dupes.Add(new RowItem
                {
                    Name = Rel(f.FullPath), FullPath = f.FullPath, Size = f.Length, SizeText = Fmt(f.Length),
                    // The first copy in each group is not offered, so a whole
                    // group can never be ticked away by accident.
                    Extra = first ? $"group {group} · keep this one" : $"group {group} · copy",
                    Selectable = !first,
                });
                first = false;
            }
        }
        TxtDupes.Text = result.Groups.Count == 0
            ? "No duplicate files found. Every file here has unique contents."
            : $"{Fmt(result.Reclaimable)} reclaimable across {result.Groups.Count:N0} group(s). One copy in each group is kept and cannot be selected."
              + (result.Unreadable.Count > 0 ? $"  {result.Unreadable.Count} file(s) could not be read and were left out." : "");
        BtnDupes.IsEnabled = true;
    }

    private void Units_Click(object sender, RoutedEventArgs e)
    {
        _binary = !_binary;
        BtnUnits.Content = _binary ? "Show GB" : "Show GiB";
        if (_tree is not null) { ClearAll(); ShowResults(); }
    }

    private void Tabs_Changed(object sender, SelectionChangedEventArgs e) => UpdateSelection();

    private IEnumerable<RowItem> AllRows() => _big.Concat(_dupes).Concat(_rebuild);

    private void UpdateSelection()
    {
        var chosen = AllRows().Where(r => r.Selected).ToList();
        long bytes = chosen.Sum(r => r.Size);
        TxtSelection.Text = chosen.Count == 0
            ? "Nothing selected."
            : $"{chosen.Count:N0} item(s) selected · {Fmt(bytes)}";
        BtnClean.IsEnabled = chosen.Count > 0;
    }

    private void Clean_Click(object sender, RoutedEventArgs e)
    {
        var chosen = AllRows().Where(r => r.Selected).ToList();
        if (chosen.Count == 0) return;

        var guard = SafetyGuard.ForThisMachine();
        var allowed = new List<RowItem>();
        var refused = new List<string>();

        foreach (var row in chosen)
        {
            if (!SafetyGuard.IsWithin(row.FullPath, _root))
            {
                refused.Add($"{row.Name} — outside the folder you scanned");
                continue;
            }
            var verdict = guard.Check(row.FullPath);
            if (verdict.Allowed) allowed.Add(row);
            else refused.Add($"{row.Name} — {verdict.Reason}");
        }

        var message = new System.Text.StringBuilder();
        if (allowed.Count > 0)
        {
            message.AppendLine($"Move {allowed.Count:N0} item(s) to the Recycle Bin, freeing about {Fmt(allowed.Sum(a => a.Size))}?");
            message.AppendLine();
            foreach (var a in allowed.Take(12)) message.AppendLine("  • " + a.Name);
            if (allowed.Count > 12) message.AppendLine($"  … and {allowed.Count - 12:N0} more");
            message.AppendLine();
            message.AppendLine("They go to the Recycle Bin, so you can put them back.");
        }
        if (refused.Count > 0)
        {
            message.AppendLine();
            message.AppendLine($"{refused.Count:N0} item(s) will NOT be touched:");
            foreach (var r in refused.Take(8)) message.AppendLine("  • " + r);
            if (refused.Count > 8) message.AppendLine($"  … and {refused.Count - 8:N0} more");
        }

        if (allowed.Count == 0)
        {
            MessageBox.Show(this, message.ToString(), "Nothing can be removed",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, message.ToString(), "Move to Recycle Bin",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK)
            return;

        var outcome = RecycleBin.Send([.. allowed.Select(a => a.FullPath)], new WindowInteropHelper(this).Handle);

        var report = new System.Text.StringBuilder();
        report.AppendLine($"{outcome.Moved:N0} item(s) moved to the Recycle Bin, freeing about {Fmt(outcome.Bytes)}.");
        if (outcome.Failures.Count > 0)
        {
            report.AppendLine();
            foreach (var f in outcome.Failures.Take(8)) report.AppendLine("  • " + f);
        }
        report.AppendLine();
        report.AppendLine("Scan again to see the new picture.");
        MessageBox.Show(this, report.ToString(), "Done", MessageBoxButton.OK, MessageBoxImage.Information);

        foreach (var row in allowed) row.Selected = false;
        UpdateSelection();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_tree is null) return;
        var dialog = new SaveFileDialog
        {
            FileName = "strata-largest-files.csv",
            Filter = "CSV file|*.csv",
        };
        if (dialog.ShowDialog(this) != true) return;

        static string Cell(object? v)
        {
            var s = v?.ToString() ?? "";
            return s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? '"' + s.Replace("\"", "\"\"") + '"' : s;
        }

        using var writer = new StreamWriter(dialog.FileName);
        writer.WriteLine("path,bytes,modified");
        foreach (var f in _tree.AllFiles().OrderByDescending(f => f.Length).Take(2000))
            writer.WriteLine($"{Cell(f.FullPath)},{f.Length},{f.LastWriteUtc:yyyy-MM-dd}");
    }
}
