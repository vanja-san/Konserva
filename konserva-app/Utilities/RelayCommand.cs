using System.Windows.Input;

namespace Konserva.Utilities;

/// <summary>
/// Простая реализация ICommand для синхронных команд
/// </summary>
public class RelayCommand : ICommand
{
  private readonly Action<object?> _execute;
  private readonly Func<object?, bool>? _canExecute;

  public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
  {
    _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    _canExecute = canExecute;
  }

  public RelayCommand(Action execute, Func<bool>? canExecute = null)
      : this(_ => execute(), canExecute is not null ? _ => canExecute() : null)
  {
  }

  public event EventHandler? CanExecuteChanged
  {
    add => CommandManager.RequerySuggested += value;
    remove => CommandManager.RequerySuggested -= value;
  }

  public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
  public void Execute(object? parameter) => _execute(parameter);

  public void NotifyCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}

/// <summary>
/// Реализация ICommand для синхронных команд с типизированным параметром
/// </summary>
public class RelayCommand<T> : ICommand
{
  private readonly Action<T?> _execute;
  private readonly Func<T?, bool>? _canExecute;

  public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
  {
    _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    _canExecute = canExecute;
  }

  public event EventHandler? CanExecuteChanged
  {
    add => CommandManager.RequerySuggested += value;
    remove => CommandManager.RequerySuggested -= value;
  }

  public bool CanExecute(object? parameter) =>
      _canExecute?.Invoke(parameter is T t ? t : default) ?? true;

  public void Execute(object? parameter)
  {
    _execute(parameter is T t ? t : default);
  }

  public void NotifyCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}

/// <summary>
/// Простая реализация ICommand для асинхронных команд
/// </summary>
public class AsyncRelayCommand : ICommand
{
  private readonly Func<object?, Task> _execute;
  private readonly Func<object?, bool>? _canExecute;
  private bool _isExecuting;

  public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
  {
    _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    _canExecute = canExecute;
  }

  public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
      : this(_ => execute(), canExecute is not null ? _ => canExecute() : null)
  {
  }

  public event EventHandler? CanExecuteChanged
  {
    add => CommandManager.RequerySuggested += value;
    remove => CommandManager.RequerySuggested -= value;
  }

  public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);

  public async void Execute(object? parameter)
  {
    if (_isExecuting)
      return;

    _isExecuting = true;
    CommandManager.InvalidateRequerySuggested();

    try
    {
      await _execute(parameter);
    }
    catch (Exception ex)
    {
      Logger.Error($"AsyncRelayCommand failed: {ex.Message}", ex, "RelayCommand");
    }
    finally
    {
      _isExecuting = false;
      CommandManager.InvalidateRequerySuggested();
    }
  }

  public void NotifyCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}

/// <summary>
/// Реализация ICommand для асинхронных команд с типизированным параметром
/// </summary>
public class AsyncRelayCommand<T> : ICommand
{
  private readonly Func<T?, Task> _execute;
  private readonly Func<T?, bool>? _canExecute;
  private bool _isExecuting;

  public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null)
  {
    _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    _canExecute = canExecute;
  }

  public event EventHandler? CanExecuteChanged
  {
    add => CommandManager.RequerySuggested += value;
    remove => CommandManager.RequerySuggested -= value;
  }

  public bool CanExecute(object? parameter)
  {
    var typed = parameter is T t ? t : default;
    return !_isExecuting && (_canExecute?.Invoke(typed) ?? true);
  }

  public async void Execute(object? parameter)
  {
    if (_isExecuting)
      return;

    _isExecuting = true;
    CommandManager.InvalidateRequerySuggested();

    try
    {
      await _execute(parameter is T t ? t : default);
    }
    catch (Exception ex)
    {
      Logger.Error($"AsyncRelayCommand<{typeof(T).Name}> failed: {ex.Message}", ex, "RelayCommand");
    }
    finally
    {
      _isExecuting = false;
      CommandManager.InvalidateRequerySuggested();
    }
  }

  /// <summary>
  /// Асинхронное выполнение команды с возможностью await
  /// </summary>
  public async Task ExecuteAsync(T? parameter)
  {
    if (_isExecuting)
      return;

    _isExecuting = true;
    CommandManager.InvalidateRequerySuggested();

    try
    {
      await _execute(parameter);
    }
    finally
    {
      _isExecuting = false;
      CommandManager.InvalidateRequerySuggested();
    }
  }

  public void NotifyCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
