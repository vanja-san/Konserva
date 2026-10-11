using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Konserva.Localization;
using Konserva.Services;
using Konserva.Utilities;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Konserva.Controls.Sections;

/// <summary>
/// Server console section: log, highlighting, sending commands.
/// </summary>
public partial class ServerConsoleSection : System.Windows.Controls.UserControl
{
    private string? _serverId;
    private bool _colorizerInitialized;
    private bool _consoleAutoScroll;
    private bool _consoleWordWrap;

    private const int LogBatchIntervalMs = 50;
    private const int LogBatchMaxChars = 64 * 1024;
    private const int MaxConsoleLines = 5000;

    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _pendingLines = new();
    private int _flushScheduled;

    public ServerConsoleSection()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sets the server ID used to send commands (the process may be missing).
    /// </summary>
    public void SetServerId(string? serverId)
    {
        _serverId = serverId;
    }

    /// <summary>
    /// Applies the console settings from the application config
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
    /// Initializes the Colorizer when the TextEditor first appears (the TextView must be ready)
    /// </summary>
    private void LogBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (LogBox.IsVisible && !_colorizerInitialized && LogBox.TextArea?.TextView != null)
        {
            // Remove the built-in LinkElementGenerator — it creates VisualLineLinkText
            // with a dark blue default color that our coloring cannot show through
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
    /// Loads the existing process log into the console.
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
    /// Appends a new log line to the console.
    /// <para>
    /// Lines are buffered and flushed in batches. A busy server emits hundreds of lines
    /// per second, and dispatching each one separately meant one dispatcher operation plus
    /// a document re-measure and ScrollToEnd per line — the UI fell behind the server and
    /// the dispatcher queue grew without bound. Batching bounds the work per tick.
    /// </para>
    /// </summary>
    public void AppendLog(string line)
    {
        _pendingLines.Enqueue(line);

        // Any pending batch already has a flush scheduled.
        if (Interlocked.Exchange(ref _flushScheduled, 1) == 1)
            return;

        _ = FlushLoopAsync();
    }

    /// <summary>
    /// Waits one batch interval, then writes every buffered line in a single operation.
    /// </summary>
    private async Task FlushLoopAsync()
    {
        try
        {
            await Task.Delay(LogBatchIntervalMs);

            var batch = new StringBuilder();
            while (_pendingLines.TryDequeue(out var line))
            {
                batch.Append(line).Append('\n');

                // Bound the batch size so a huge backlog does not produce one giant
                // document edit that blocks the dispatcher for a visible time.
                if (batch.Length >= LogBatchMaxChars)
                    break;
            }

            if (batch.Length > 0)
            {
                var text = batch.ToString();
                await this.InvokeAsync(() => InsertText(text));
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"[AppendLog] Error: {ex.Message}", "ServerConsoleSection");
        }
        finally
        {
            // Allow the next line to schedule another flush; if lines arrived while
            // this batch was running, restart immediately.
            Interlocked.Exchange(ref _flushScheduled, 0);

            if (!_pendingLines.IsEmpty && Interlocked.Exchange(ref _flushScheduled, 1) == 0)
                _ = FlushLoopAsync();
        }
    }

    /// <summary>
    /// Inserts a batch of text at the end of the console. Must run on the UI thread.
    /// </summary>
    private void InsertText(string text)
    {
        if (_consoleAutoScroll)
        {
            LogBox.Document.Insert(LogBox.Document.TextLength, text);
            ConsolePlaceholder.Visibility = Visibility.Collapsed;
            LogBox.ScrollToEnd();
        }
        else
        {
            double oldOffset = LogBox.VerticalOffset;
            LogBox.Document.Insert(LogBox.Document.TextLength, text);
            ConsolePlaceholder.Visibility = Visibility.Collapsed;
            LogBox.ScrollToVerticalOffset(oldOffset);
        }

        TrimDocument();
    }

    /// <summary>
    /// Keeps the editor document bounded. Without this the document grew for the whole
    /// session (unlike the server-side log buffer, which is capped), so every insert at the
    /// tail paid an ever-growing redraw cost.
    /// </summary>
    private void TrimDocument()
    {
        var document = LogBox.Document;
        if (document.LineCount <= MaxConsoleLines)
            return;

        int removeTo = document.LineCount - MaxConsoleLines;
        var firstKeptLine = document.GetLineByNumber(removeTo + 1);
        document.Remove(firstKeptLine.Offset, document.TextLength - firstKeptLine.Offset);
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
    /// Shows or hides the command input area
    /// (the input field + the send button) depending on the server status.
    /// </summary>
    public void UpdateCommandInputVisibility(bool isRunning)
    {
        CommandInputRow.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// A click outside the input area (for example, on the log or on empty space) clears
    /// the focus from the input field — otherwise in WPF it stays on the last
    /// focusable element.
    /// </summary>
    private void ConsoleSection_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && CommandInputRow.IsAncestorOf(source))
            return;

        Keyboard.ClearFocus();
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

            // 1. Timestamp [HH:MM:SS] — gray
            var tsMatch = TimestampRegex.Match(lineText);
            if (tsMatch.Success)
            {
                ChangeLinePart(
                    line.Offset + tsMatch.Index,
                    line.Offset + tsMatch.Index + tsMatch.Length,
                    e => e.TextRunProperties.SetForegroundBrush(TimestampBrush));
            }

            // 2. [STDERR] — orange
            var stderrMatch = StderrTagRegex.Match(lineText);
            if (stderrMatch.Success)
            {
                ChangeLinePart(
                    line.Offset + stderrMatch.Index,
                    line.Offset + stderrMatch.Index + stderrMatch.Length,
                    e => e.TextRunProperties.SetForegroundBrush(WarnBrush));
            }

            // 2a. Level word (WARNING:, ERROR:, etc.) after [STDERR]
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

            // 3. Level block [LEVEL] or [thread/LEVEL] — with its own color
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

            // 4. URLs — a nice light blue (over the default dark blue)
            foreach (Match urlMatch in UrlRegex.Matches(lineText))
            {
                ChangeLinePart(
                    line.Offset + urlMatch.Index,
                    line.Offset + urlMatch.Index + urlMatch.Length,
                    e => e.TextRunProperties.SetForegroundBrush(InfoBrush));
            }

            // 5. Lines without special tags — we check the whole start/stop line
            if (!tsMatch.Success && !levelMatch.Success && !stderrMatch.Success &&
                ContainsMilestoneText(lineText))
            {
                ChangeLinePart(
                    line.Offset,
                    line.EndOffset,
                    e => e.TextRunProperties.SetForegroundBrush(SuccessBrush));
            }
        }

        /// <summary>
        /// Localized milestone strings, refreshed once per language instead of on every
        /// rendered line. <see cref="LocalizationManager.Get"/> takes a lock and does several
        /// dictionary lookups, which this hot path called up to three times per line.
        /// </summary>
        private static readonly Lock MilestoneLock = new();
        private static string _lastMilestoneLanguage = string.Empty;
        private static string _serverStarted = string.Empty;
        private static string _serverReady = string.Empty;
        private static string _serverStopped = string.Empty;

        private static void RefreshMilestonesIfLanguageChanged()
        {
            var language = LocalizationManager.CurrentCulture.Name;

            lock (MilestoneLock)
            {
                if (string.Equals(language, _lastMilestoneLanguage, StringComparison.Ordinal))
                    return;

                _serverStarted = LocalizationManager.Get("Log_ServerStarted");
                _serverReady = LocalizationManager.Get("Log_ServerReady");
                _serverStopped = LocalizationManager.Get("Log_ServerStoppedSuccessfully");
                _lastMilestoneLanguage = language;
            }
        }

        private static bool ContainsMilestoneText(string lineText)
        {
            RefreshMilestonesIfLanguageChanged();

            lock (MilestoneLock)
            {
                return lineText.Contains(_serverStarted, StringComparison.Ordinal) ||
                       lineText.Contains(_serverReady, StringComparison.Ordinal) ||
                       lineText.Contains(_serverStopped, StringComparison.Ordinal);
            }
        }
    }
}