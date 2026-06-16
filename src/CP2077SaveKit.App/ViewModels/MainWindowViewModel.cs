using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CP2077SaveKit.Core;

namespace CP2077SaveKit.App.ViewModels;

/// <summary>One editable inventory row. Editing Quantity flags the VM dirty.</summary>
public partial class ItemRow : ObservableObject
{
    public ulong Hash { get; init; }
    public string Display { get; init; } = "";
    [ObservableProperty] private uint _quantity;
    public string HashHex => $"0x{Hash:X16}";
}

public partial class MainWindowViewModel : ObservableObject
{
    private SaveFile? _save;

    [ObservableProperty] private string _status = "Open a save to begin.";
    [ObservableProperty] private string? _loadedPath;
    [ObservableProperty] private uint _money;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _isLoaded;

    private readonly List<ItemRow> _allItems = new();
    public ObservableCollection<ItemRow> Items { get; } = new();

    partial void OnSearchChanged(string value) => ApplyFilter();

    public void LoadFromPath(string path)
    {
        try
        {
            _save = SaveFile.Load(path);
            LoadedPath = path;
            IsLoaded = true;

            _allItems.Clear();
            foreach (var sub in InventoryReader.Read(_save))
                foreach (var it in sub.Items)
                    _allItems.Add(new ItemRow { Hash = it.IdHash, Display = it.Display, Quantity = it.Quantity });

            Money = InventoryEditor.GetQuantity(_save, InventoryEditor.MoneyHash);
            ApplyFilter();
            var named = _allItems.Count(i => !i.Display.StartsWith("<unresolved"));
            Status = $"Loaded {Path.GetFileName(Path.GetDirectoryName(path))} — v{_save.GameVersion}, {_allItems.Count} items ({named} named).";
        }
        catch (Exception ex)
        {
            Status = $"ERROR loading: {ex.Message}";
            IsLoaded = false;
        }
    }

    private void ApplyFilter()
    {
        Items.Clear();
        IEnumerable<ItemRow> q = _allItems;
        if (!string.IsNullOrWhiteSpace(Search))
            q = q.Where(i => i.Display.Contains(Search, StringComparison.OrdinalIgnoreCase)
                          || i.HashHex.Contains(Search, StringComparison.OrdinalIgnoreCase));
        foreach (var i in q.Take(500)) Items.Add(i);
    }

    /// <summary>Apply edits to the in-memory save and write to disk, backing up the original first.</summary>
    public void SaveToPath(string path)
    {
        if (_save is null) { Status = "Nothing loaded."; return; }
        try
        {
            InventoryEditor.SetMoney(_save, Money);
            foreach (var row in _allItems)
                InventoryEditor.SetQuantity(_save, row.Hash, row.Quantity);

            if (File.Exists(path))
            {
                var bak = path + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.Copy(path, bak, overwrite: false);
            }
            _save.Save(path);
            Status = $"Saved to {path} (backup made). Load it in-game to verify.";
        }
        catch (Exception ex)
        {
            Status = $"ERROR saving: {ex.Message}";
        }
    }

    public static string DefaultSavesDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "CD Projekt Red", "Cyberpunk 2077", "saves");
}
