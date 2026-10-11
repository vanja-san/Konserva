using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using System.Collections.ObjectModel;

using ObservableObject = CommunityToolkit.Mvvm.ComponentModel.ObservableObject;

namespace Konserva.ViewModels;

/// <summary>
/// ViewModel для страницы списка серверов
/// </summary>
public partial class ServersViewModel : ObservableObject, IDisposable
{
    private readonly IServerManager _serverManager;
    private readonly HashSet<string> _busyServers = [];
    private bool _disposed;

    public ServersViewModel(IServerManager serverManager)
    {
        _serverManager = serverManager;

        _serverManager.OnServersChanged += OnServersChanged;

        RefreshList();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _serverManager.OnServersChanged -= OnServersChanged;
    }

    /// <summary>
    /// Отфильтрованный список серверов
    /// </summary>
    public ObservableCollection<Server> FilteredServers { get; } = [];

    /// <summary>
    /// Флаг: нет серверов
    /// </summary>
    public bool HasNoServers => FilteredServers.Count == 0;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _filterType = "All";

    [ObservableProperty]
    private string _filterStatus = "All";

    [ObservableProperty]
    private string _sortField = "Name";

    [ObservableProperty]
    private bool _sortAscending = true;

    [ObservableProperty]
    private bool _isVisible = true;

    // События для UI (страница подписывается на них)
    public event Action<Server>? NavigateToServerRequested;
    public event Action<Server>? OpenFolderRequested;

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnFilterTypeChanged(string value) => ApplyFilters();
    partial void OnFilterStatusChanged(string value) => ApplyFilters();
    partial void OnSortFieldChanged(string value) => ApplyFilters();
    partial void OnSortAscendingChanged(bool value) => ApplyFilters();

    partial void OnIsVisibleChanged(bool value)
    {
        if (value)
            RefreshList();
    }

    private bool CanPlay(Server? server) =>
        server is not null && !_busyServers.Contains(server.Id);

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync(Server? server)
    {
        if (server is null || !TryBeginOperation(server.Id))
            return;

        try
        {
            if (server.IsRunning)
                await _serverManager.StopServerAsync(server.Id);
            else
                await _serverManager.StartServerAsync(server.Id);
        }
        finally
        {
            EndOperation(server.Id);
        }
    }

    /// <summary>
    /// Marks a server as busy and refreshes the affected commands.
    /// Atomic on <c>_busyServers</c> so a double click cannot start two servers,
    /// and notifies the command states because the busy flag changed.
    /// </summary>
    private bool TryBeginOperation(string serverId)
    {
        if (!_busyServers.Add(serverId))
            return false;

        RaiseCommandStates();
        return true;
    }

    private void EndOperation(string serverId)
    {
        if (_busyServers.Remove(serverId))
            RaiseCommandStates();
    }

    /// <summary>
    /// Re-evaluates CanExecute for every command in this ViewModel.
    /// Without it the buttons keep their previous enabled state after a busy flag flips.
    /// </summary>
    private void RaiseCommandStates()
    {
        PlayCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private bool CanDelete(Server? server) =>
        server is not null && !_busyServers.Contains(server.Id);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync(Server? server)
    {
        if (server is null || !TryBeginOperation(server.Id))
            return;

        try
        {
            await _serverManager.DeleteServerAsync(server.Id);
        }
        finally
        {
            EndOperation(server.Id);
        }
    }

    [RelayCommand]
    private void OpenFolder(Server? server)
    {
        if (server is not null)
            OpenFolderRequested?.Invoke(server);
    }

    [RelayCommand]
    private void NavigateToServer(Server? server)
    {
        if (server is not null)
            NavigateToServerRequested?.Invoke(server);
    }

    [RelayCommand]
    private void ToggleFilter(string? filterType)
    {
        if (filterType is null)
            return;

        FilterType = FilterType == filterType ? "All" : filterType;
    }

    /// <summary>
    /// Обновить список серверов
    /// </summary>
    public void RefreshList() => ApplyFilters();

    private void ApplyFilters()
    {
        var servers = _serverManager.GetServers();

        var filtered = servers.Where(s =>
        {
            var matchSearch = string.IsNullOrEmpty(SearchText) ||
                              s.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase);

            var matchType = FilterType == "All" ||
                            s.ModLoader.Type.ToString().Equals(FilterType, StringComparison.OrdinalIgnoreCase);

            var matchStatus = FilterStatus == "All" ||
                              (FilterStatus == "Running" && s.IsRunning) ||
                              (FilterStatus == "Stopped" && !s.IsRunning);

            return matchSearch && matchType && matchStatus;
        });

        // Sorting
        filtered = SortField switch
        {
            "Status" => SortAscending
                ? filtered.OrderBy(s => s.IsRunning ? 0 : 1)
                : filtered.OrderBy(s => s.IsRunning ? 1 : 0),
            "Type" => SortAscending
                ? filtered.OrderBy(s => s.ModLoader.Type.ToString())
                : filtered.OrderByDescending(s => s.ModLoader.Type.ToString()),
            "Version" => SortAscending
                ? filtered.OrderBy(s => s.McVersion)
                : filtered.OrderByDescending(s => s.McVersion),
            _ => SortAscending
                ? filtered.OrderBy(s => s.Name)
                : filtered.OrderByDescending(s => s.Name),
        };

        var result = filtered.ToList();

        // Rebuilding the collection unconditionally raised N+1 CollectionChanged events on
        // every keystroke, so the ItemsControl recreated every container each time. Only
        // touch the collection when the visible set or its order actually changed.
        bool sameContent = result.Count == FilteredServers.Count;
        if (sameContent)
        {
            for (int i = 0; i < result.Count; i++)
            {
                if (!ReferenceEquals(result[i], FilteredServers[i]))
                {
                    sameContent = false;
                    break;
                }
            }
        }

        if (!sameContent)
        {
            FilteredServers.Clear();
            foreach (var server in result)
            {
                FilteredServers.Add(server);
            }

            OnPropertyChanged(nameof(HasNoServers));
        }
    }

    // ─── Dispose ────────────────────────────────────────────────────

    private void OnServersChanged()
    {
        var servers = _serverManager.GetServers();

        // Index the visible list once instead of scanning it per server: the previous
        // nested FirstOrDefault made every status change O(n²) in the number of servers.
        var visible = new Dictionary<string, Server>(FilteredServers.Count, StringComparer.Ordinal);
        foreach (var s in FilteredServers)
        {
            visible[s.Id] = s;
        }

        // Clear the busy flag for servers that are not running
        foreach (var server in servers)
        {
            if (!server.IsRunning)
                _busyServers.Remove(server.Id);

            // Server instances are shared with the manager, but the visible entry may be a
            // stale copy from an earlier filter pass — keep the status in sync either way.
            if (visible.TryGetValue(server.Id, out var listed))
                listed.Status = server.Status;
        }

        if (!IsVisible)
        {
            // The page is hidden: statuses are refreshed above without resetting the order.
            return;
        }

        ApplyFilters();
    }
}
