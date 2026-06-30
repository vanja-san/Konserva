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
    _serverManager.OnServerStartError += OnServerStartError;

    RefreshList();
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;

    _serverManager.OnServersChanged -= OnServersChanged;
    _serverManager.OnServerStartError -= OnServerStartError;
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
  public event Action<Server, string>? ShowErrorRequested;

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
    if (server is null)
      return;

    _busyServers.Add(server.Id);

    try
    {
      if (server.IsRunning)
        _serverManager.StopServer(server.Id);
      else
        _serverManager.StartServer(server.Id);
    }
    finally
    {
      _busyServers.Remove(server.Id);
    }
  }

  private bool CanDelete(Server? server) =>
      server is not null && !_busyServers.Contains(server.Id);

  [RelayCommand(CanExecute = nameof(CanDelete))]
  private async Task DeleteAsync(Server? server)
  {
    if (server is null)
      return;

    _busyServers.Add(server.Id);

    try
    {
      await _serverManager.DeleteServerAsync(server.Id);
    }
    finally
    {
      _busyServers.Remove(server.Id);
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

    // Сортировка
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

    FilteredServers.Clear();
    foreach (var server in filtered)
    {
      FilteredServers.Add(server);
    }

    OnPropertyChanged(nameof(HasNoServers));
  }

  // ─── Dispose ────────────────────────────────────────────────────

  private void OnServersChanged()
  {
    // Снимаем блокировку для серверов, которые не запущены
    foreach (var server in _serverManager.GetServers().Where(s => !s.IsRunning))
    {
      _busyServers.Remove(server.Id);
    }

    if (!IsVisible)
    {
      // Если страница скрыта — только обновляем статусы, не сбрасывая порядок
      var currentServers = _serverManager.GetServers();
      foreach (var server in currentServers)
      {
        var existing = FilteredServers.FirstOrDefault(s => s.Id == server.Id);
        if (existing != null)
          existing.Status = server.Status;
      }
      return;
    }

    ApplyFilters();
  }

  private void OnServerStartError(Server server, string errorMessage)
  {
    Logger.Info($"[ServersViewModel] Error for server {server.Name}: {errorMessage}", "ServersViewModel");
    ShowErrorRequested?.Invoke(server, errorMessage);
  }
}
