using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Konserva.Localization;
using Konserva.Services;
using Konserva.Utilities;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Konserva.Controls.Sections;

/// <summary>
/// Секция консоли сервера: лог, подсветка, отправка команд.
/// </summary>
public partial class ServerConsoleSection : System.Windows.Controls.UserControl
{
    private string? _serverId;
    private bool _colorizerInitialized;
    private bool _consoleAutoScroll;
    private bool _consoleWordWrap;

    public ServerConsoleSection()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Задаёт ID сервера для отправки команд (процесс может отсутствовать).
    /// </summary>
    public void SetServerId(string? serverId)
    {
        _serverId = serverId;
    }

    /// <summary>
    /// Применяет настройки консоли из конфига приложения
    /// </summary>
    public void ApplyConsoleSettings()
    {
        var config = Ioc.Default.GetService<IConfigService>()?.GetConfig();
        if (config == null)
            return;

        _consoleAutoScroll = config.ConsoleAutoScroll;
        _consoleWordWrap = config.ConsoleWordWrap;
        LogBox.WordWrap = _consoleWordWrap;
    }

    /// <summary>
    /// Инициализация Colorizer при первом появлении TextEditor (TextView должен быть готов)
    /// </summary>
    private void LogBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (LogBox.IsVisible && !_colorizerInitialized && LogBox.TextArea?.TextView != null)
        {
            // Удаляем встроенный LinkElementGenerator — он создаёт VisualLineLinkText
            // с тёмно-синим цветом по умолчанию, поверх которого наша раскраска не работает
            var linkGen = LogBox.TextArea.TextView.ElementGenerators
                .OfType<ICSharpCode.AvalonEdit.Rendering.LinkElementGenerator>()
                .FirstOrDefault();
            if (linkGen != null)
                LogBox.TextArea.TextView.ElementGenerators.Remove(linkGen);

            LogBox.TextArea.TextView.LineTransformers.Add(new LogColorizer());
            _colorizerInitialized = true;
        }
    }

    /// <summary>
    /// Загружает существующий лог процесса в консоль.
    /// </summary>
    public void LoadExistingLogs(IReadOnlyList<string> logs)
    {
        this.Invoke(() =>
        {
            double oldOffset = LogBox.VerticalOffset;

            if (logs.Count > 0)
            {
                LogBox.Document.Text = string.Join("\n", logs) + "\n";
                ConsolePlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                LogBox.Clear();
                ConsolePlaceholder.Visibility = Visibility.Visible;
            }

            if (_consoleAutoScroll)
                LogBox.ScrollToEnd();
            else
                LogBox.ScrollToVerticalOffset(oldOffset);
        });
    }

    /// <summary>
    /// Добавляет новую строку лога в консоль.
    /// </summary>
    public async void AppendLog(string line)
    {
        try
        {
            await this.InvokeAsync(() =>
            {
                if (_consoleAutoScroll)
                {
                    LogBox.Document.Insert(LogBox.Document.TextLength, line + "\n");
                    ConsolePlaceholder.Visibility = Visibility.Collapsed;
                    LogBox.ScrollToEnd();
                }
                else
                {
                    double oldOffset = LogBox.VerticalOffset;
                    LogBox.Document.Insert(LogBox.Document.TextLength, line + "\n");
                    ConsolePlaceholder.Visibility = Visibility.Collapsed;
                    LogBox.ScrollToVerticalOffset(oldOffset);
                }
            });
        }
        catch (TaskCanceledException)
        {
            // Игнорируем — приложение закрывается, диспетчер уже не работает
        }
        catch (Exception ex)
        {
            Logger.Warning($"[AppendLog] Error: {ex.Message}", "ServerConsoleSection");
        }
    }

    private void SendCommand_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(CommandBox.Text))
            return;

        Ioc.Default.GetService<IServerManager>()!.SendCommand(_serverId!, CommandBox.Text);
        CommandBox.Clear();
    }

    private void CommandBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SendCommand_Click(sender, e);
        }
    }

    private void CommandBox_GotFocus(object sender, RoutedEventArgs e)
    {
        UpdateCommandPlaceholder();
    }

    private void CommandBox_LostFocus(object sender, RoutedEventArgs e)
    {
        UpdateCommandPlaceholder();
    }

    private void CommandBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCommandPlaceholder();
    }

    private void UpdateCommandPlaceholder()
    {
        CommandPlaceholder.Visibility = (string.IsNullOrEmpty(CommandBox.Text) && !CommandBox.IsFocused)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// Показывает или скрывает область ввода команды
    /// (поле ввода + кнопку отправки) в зависимости от статуса сервера.
    /// </summary>
    public void UpdateCommandInputVisibility(bool isRunning)
    {
        CommandInputRow.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed class LogColorizer : DocumentColorizingTransformer
    {
        private static readonly Brush TimestampBrush =
            new SolidColorBrush(Color.FromRgb(140, 140, 140));
        private static readonly Brush InfoBrush =
            new SolidColorBrush(Color.FromRgb(100, 170, 255));
        private static readonly Brush WarnBrush =
            new SolidColorBrush(Color.FromRgb(255, 200, 80));
        private static readonly Brush ErrorBrush =
            new SolidColorBrush(Color.FromRgb(220, 100, 100));
        private static readonly Brush DebugBrush =
            new SolidColorBrush(Color.FromRgb(160, 120, 200));
        private static readonly Brush SuccessBrush =
            new SolidColorBrush(Color.FromRgb(80, 200, 80));

        private static readonly Regex TimestampRegex =
            new Regex(@"^\[\d{2}:\d{2}:\d{2}\]", RegexOptions.Compiled);
        private static readonly Regex LevelTagRegex =
            new Regex(@"\[(?:[^\]/]*/)?(INFO|WARN(?:ING)?|ERROR|FATAL|DEBUG)\]", RegexOptions.Compiled);
        private static readonly Regex StderrTagRegex =
            new Regex(@"^\[STDERR\]", RegexOptions.Compiled);
        private static readonly Regex StderrLevelRegex =
            new Regex(@"(WARNING|ERROR|INFO|FATAL|DEBUG):", RegexOptions.Compiled);
        private static readonly Regex UrlRegex =
            new Regex(@"https?://[^\s\]\)<>]+", RegexOptions.Compiled);

        protected override void ColorizeLine(DocumentLine line)
        {
            var lineText = CurrentContext.Document.GetText(line);

            // 1. Таймштамп [HH:MM:SS] — серым
            var tsMatch = TimestampRegex.Match(lineText);
            if (tsMatch.Success)
            {
                ChangeLinePart(
                    line.Offset + tsMatch.Index,
                    line.Offset + tsMatch.Index + tsMatch.Length,
                    e => e.TextRunProperties.SetForegroundBrush(TimestampBrush));
            }

            // 2. [STDERR] — оранжевым
            var stderrMatch = StderrTagRegex.Match(lineText);
            if (stderrMatch.Success)
            {
                ChangeLinePart(
                    line.Offset + stderrMatch.Index,
                    line.Offset + stderrMatch.Index + stderrMatch.Length,
                    e => e.TextRunProperties.SetForegroundBrush(WarnBrush));
            }

            // 2a. Уровневое слово (WARNING:, ERROR:, etc.) после [STDERR]
            foreach (Match slMatch in StderrLevelRegex.Matches(lineText))
            {
                var slTag = slMatch.Groups[1].Value;
                var slBrush = slTag switch
                {
                    "ERROR" or "FATAL" => ErrorBrush,
                    "WARNING" => WarnBrush,
                    "INFO" => InfoBrush,
                    "DEBUG" => DebugBrush,
                    _ => null
                };

                if (slBrush != null)
                {
                    ChangeLinePart(
                        line.Offset + slMatch.Index,
                        line.Offset + slMatch.Index + slMatch.Groups[1].Length,
                        e => e.TextRunProperties.SetForegroundBrush(slBrush));
                }
            }

            // 3. Уровневый блок [LEVEL] или [thread/LEVEL] — своим цветом
            var levelMatch = LevelTagRegex.Match(lineText);
            if (levelMatch.Success)
            {
                var tag = levelMatch.Groups[1].Value;
                var brush = tag switch
                {
                    "ERROR" or "FATAL" => ErrorBrush,
                    "WARN" or "WARNING" => WarnBrush,
                    "INFO" => InfoBrush,
                    "DEBUG" => DebugBrush,
                    _ => null
                };

                if (brush != null)
                {
                    ChangeLinePart(
                        line.Offset + levelMatch.Index,
                        line.Offset + levelMatch.Index + levelMatch.Length,
                        e => e.TextRunProperties.SetForegroundBrush(brush));
                }
            }

            // 4. URL-адреса — красивым голубым (поверх стандартного тёмно-синего)
            foreach (Match urlMatch in UrlRegex.Matches(lineText))
            {
                ChangeLinePart(
                    line.Offset + urlMatch.Index,
                    line.Offset + urlMatch.Index + urlMatch.Length,
                    e => e.TextRunProperties.SetForegroundBrush(InfoBrush));
            }

            // 5. Строки без спецтегов — проверяем старт/стоп целиком
            if (!tsMatch.Success && !levelMatch.Success && !stderrMatch.Success)
            {
                var text = lineText.AsSpan();
                if (text.Contains(LocalizationManager.Get("Log_ServerStarted").AsSpan(), StringComparison.Ordinal) ||
                    text.Contains(LocalizationManager.Get("Log_ServerReady").AsSpan(), StringComparison.Ordinal) ||
                    text.Contains(LocalizationManager.Get("Log_ServerStoppedSuccessfully").AsSpan(), StringComparison.Ordinal))
                {
                    ChangeLinePart(
                        line.Offset,
                        line.EndOffset,
                        e => e.TextRunProperties.SetForegroundBrush(SuccessBrush));
                }
            }
        }
    }
}