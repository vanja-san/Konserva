using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Konserva.ViewModels;

/// <summary>
/// ViewModel для страницы списка серверов
/// </summary>
public class ServersViewModel : ObservableObject
{
  private readonly IServerManager _serverManager;
  private string _searchText = string.Empty;
  private string _filterType = "All";
  private string _filterStatus = "All";
  private readonly HashSet<string> _busyServers = [];
  private bool _isVisible = true;

  public ServersViewModel(IServerManager serverManager)
  {
    _serverManager = serverManager;

    PlayCommand = new AsyncRelayCommand<Server>(PlayAsync, CanPlay);
    DeleteCommand = new AsyncRelayCommand<Server>(DeleteAsync, CanDelete);
    OpenFolderCommand = new RelayCommand<Server>(OpenFolder, CanOpenFolder);
    NavigateToServerCommand = new RelayCommand<Server>(NavigateToServer);
    ToggleFilterCommand = new RelayCommand<string>(ToggleFilter);

    _serverManager.OnServersChanged += OnServersChanged;
    _serverManager.OnServerStartError += OnServerStartError;

    RefreshList();
  }

  /// <summary>
  /// Отфильтрованный список серверов
  /// </summary>
  public ObservableCollection<Server> FilteredServers { get; } = [];

  /// <summary>
  /// Флаг: нет серверов
  /// </summary>
  public bool HasNoServers => FilteredServers.Count == 0;

  /// <summary>
  /// Текст поиска
  /// </summary>
  public string SearchText
  {
    get => _searchText;
    set
    {
      if (SetProperty(ref _searchText, value))
        ApplyFilters();
    }
  }

  /// <summary>
  /// Фильтр по типу (All, Vanilla, Forge, ...)
  /// </summary>
  public string FilterType
  {
    get => _filterType;
    set
    {
      if (SetProperty(ref _filterType, value))
        ApplyFilters();
    }
  }

  /// <summary>
  /// Фильтр по статусу (All, Running, Stopped)
  /// </summary>
  public string FilterStatus
  {
    get => _filterStatus;
    set
    {
      if (SetProperty(ref _filterStatus, value))
        ApplyFilters();
    }
  }

  // Команды
  public ICommand PlayCommand { get; }
  public ICommand DeleteCommand { get; }
  public ICommand OpenFolderCommand { get; }
  public ICommand NavigateToServerCommand { get; }
  public ICommand ToggleFilterCommand { get; }

  // События для UI (страница подписывается на них)
  public event Action<Server>? NavigateToServerRequested;
  public event Action<Server>? OpenFolderRequested;
  public event Action<Server, string>? ShowErrorRequested;

  private bool CanPlay(Server? server) =>
      server is not null && !_busyServers.Contains(server.Id);

  private async Task PlayAsync(Server? server)
  {
    if (server is null || !CanPlay(server))
      return;

    _busyServers.Add(server.Id);
    CommandManager.InvalidateRequerySuggested();

    try
    {
      if (server.IsRunning)
      {
        _serverManager.StopServer(server.Id);
      }
      else
      {
        _serverManager.StartServer(server.Id);
      }
    }
    finally
    {
      _busyServers.Remove(server.Id);
      CommandManager.InvalidateRequerySuggested();
    }

    await Task.CompletedTask;
  }

  private bool CanDelete(Server? server) =>
      server is not null && !_busyServers.Contains(server.Id);

  private async Task DeleteAsync(Server? server)
  {
    if (server is null || !CanDelete(server))
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

  private bool CanOpenFolder(Server? server) => server is not null;

  private void OpenFolder(Server? server)
  {
    if (server is null)
      return;

    OpenFolderRequested?.Invoke(server);
  }

  private void NavigateToServer(Server? server)
  {
    if (server is not null)
      NavigateToServerRequested?.Invoke(server);
  }

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
      var matchSearch = string.IsNullOrEmpty(_searchText) ||
                            s.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase);

      var matchType = _filterType == "All" ||
                          s.ModLoader.Type.ToString().Equals(_filterType, StringComparison.OrdinalIgnoreCase);

      var matchStatus = _filterStatus == "All" ||
                            (_filterStatus == "Running" && s.IsRunning) ||
                            (_filterStatus == "Stopped" && !s.IsRunning);

      return matchSearch && matchType && matchStatus;
    }).ToList();

    FilteredServers.Clear();
    foreach (var server in filtered)
    {
      FilteredServers.Add(server);
    }

    OnPropertyChanged(nameof(HasNoServers));
  }

  /// <summary>
  /// Флаг видимости страницы. Когда страница скрыта (навигация на другую страницу),
  /// OnServersChanged не перестраивает FilteredServers, а только обновляет статусы.
  /// </summary>
  public bool IsVisible
  {
    get => _isVisible;
    set
    {
      if (_isVisible == value) return;
      _isVisible = value;

      if (_isVisible)
      {
        // При возврате на страницу перестраиваем список с актуальным порядком
        RefreshList();
      }
    }
  }

  private void OnServersChanged()
  {
    // Снимаем блокировку для серверов, которые не запущены
    foreach (var server in _serverManager.GetServers().Where(s => !s.IsRunning))
    {
      _busyServers.Remove(server.Id);
    }

    if (!_isVisible)
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
