using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    [ObservableProperty] private bool _isBusy;

    public MainWindowViewModel()
    {
        // Warm the name dictionaries off the UI thread so the first save load is fast.
        Task.Run(() => { _ = AioCatalog.Shared; _ = TweakDbNames.Shared; });
    }

    private readonly List<ItemRow> _allItems = new();
    public ObservableCollection<ItemRow> Items { get; } = new();

    // --- Add-Item picker (stackable items) ---
    [ObservableProperty] private string _catalogSearch = "";
    [ObservableProperty] private CatalogItem? _selectedCatalogItem;
    [ObservableProperty] private uint _addQuantity = 1;
    public ObservableCollection<CatalogItem> CatalogResults { get; } = new();

    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnCatalogSearchChanged(string value) => ApplyCatalogFilter();

    private void ApplyCatalogFilter()
    {
        CatalogResults.Clear();
        foreach (var c in AioCatalog.Shared.SearchStackable(CatalogSearch, 300))
            CatalogResults.Add(c);
    }

    [RelayCommand]
    private void AddSelectedItem()
    {
        if (_save is null) { Status = "Open a save first."; return; }
        if (SelectedCatalogItem is null) { Status = "Pick an item from the list first."; return; }
        var item = SelectedCatalogItem;
        var hash = TweakDbNames.TweakHash(item.Id);
        var qty = AddQuantity == 0 ? 1u : AddQuantity;
        if (!InventoryEditor.AddOrSetStackable(_save, hash, qty))
        {
            Status = "Could not add (no inventory loaded).";
            return;
        }
        // reflect in the inventory list
        var existing = _allItems.FirstOrDefault(r => r.Hash == hash);
        if (existing is not null) existing.Quantity = qty;
        else _allItems.Add(new ItemRow { Hash = hash, Display = item.Name ?? item.Id, Quantity = qty });
        ApplyFilter();
        Status = $"Added {item.Name ?? item.Id} ×{qty} (unsaved — use Save As… then load in-game).";
    }

    public async Task LoadFromPathAsync(string path)
    {
        if (IsBusy) return;
        IsBusy = true;
        IsLoaded = false;
        Status = "Loading save…";
        try
        {
            // Heavy work (parse + name resolution for ~1600 items) off the UI thread.
            var (save, rows, money) = await Task.Run(() =>
            {
                var s = SaveFile.Load(path);
                var list = new List<ItemRow>();
                foreach (var sub in InventoryReader.Read(s))
                    foreach (var it in sub.Items)
                        list.Add(new ItemRow { Hash = it.IdHash, Display = it.Display, Quantity = it.Quantity });
                var m = InventoryEditor.GetQuantity(s, InventoryEditor.MoneyHash);
                return (s, list, m);
            });

            // Back on the UI thread: publish results.
            _save = save;
            LoadedPath = path;
            _allItems.Clear();
            _allItems.AddRange(rows);
            Money = money;
            ApplyFilter();
            ApplyCatalogFilter();
            IsLoaded = true;
            var named = _allItems.Count(i => !i.Display.StartsWith("<unresolved"));
            Status = $"Loaded {Path.GetFileName(Path.GetDirectoryName(path))} — v{save.GameVersion}, {_allItems.Count} items ({named} named).";
        }
        catch (Exception ex)
        {
            Status = $"ERROR loading: {ex.Message}";
            IsLoaded = false;
        }
        finally { IsBusy = false; }
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
    public async Task SaveToPathAsync(string path)
    {
        if (_save is null) { Status = "Nothing loaded."; return; }
        if (IsBusy) return;
        IsBusy = true;
        Status = "Saving…";
        try
        {
            var save = _save;
            var money = Money;
            var edits = _allItems.Select(r => (r.Hash, r.Quantity)).ToArray();
            await Task.Run(() =>
            {
                InventoryEditor.SetMoney(save, money);
                foreach (var (hash, qty) in edits)
                    InventoryEditor.SetQuantity(save, hash, qty);
                if (File.Exists(path))
                    File.Copy(path, path + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"), overwrite: false);
                save.Save(path);
            });
            Status = $"Saved to {path} (backup made). Load it in-game to verify.";
        }
        catch (Exception ex)
        {
            Status = $"ERROR saving: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    public static string DefaultSavesDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "CD Projekt Red", "Cyberpunk 2077", "saves");
}
