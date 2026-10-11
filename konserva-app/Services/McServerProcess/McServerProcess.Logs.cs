using Konserva.Localization;
using Konserva.Models;
using Konserva.Utilities;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Konserva.Services;

/// <summary>
/// Output handling, log parsing, process exit monitoring
/// </summary>
public partial class McServerProcess
{
    private bool _captureModIncompatibility;

    private void OnOutput(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
            return;

        var trimmed = e.Data.TrimStart();
        bool isStackTraceLine = trimmed.StartsWith("at ", StringComparison.Ordinal);

        // Mod incompatibility detected — capture the whole block (header, solutions,
        // details) up to the stack trace, not just the lines with keywords.
        if (ModIncompatibilityParser.IsModIncompatibility(e.Data))
            _captureModIncompatibility = true;

        bool capture = (_captureModIncompatibility && !isStackTraceLine)
            || e.Data.Contains("error", StringComparison.OrdinalIgnoreCase)
            || e.Data.Contains("exception", StringComparison.OrdinalIgnoreCase)
            || e.Data.Contains("failed", StringComparison.OrdinalIgnoreCase);

        if (capture)
            CaptureErrorOutput(e.Data);

        if (_captureModIncompatibility && isStackTraceLine)
            _captureModIncompatibility = false;

        AppendLog(e.Data);
        ParseOutput(e.Data);
    }

    /// <summary>
    /// Appends a line to the pending error buffer.
    /// The stdout and stderr readers run on two independent threads, so string
    /// concatenation here must be serialized — otherwise whole stack-trace
    /// fragments are silently lost and the user sees a truncated error.
    /// </summary>
    private void CaptureErrorOutput(string line)
    {
        lock (_lock)
        {
            _pendingErrorOutput += line + "\n";
        }
    }

    private void OnError(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
            return;

        var errorLine = $"[STDERR] {e.Data}";
        AppendLog(errorLine);
        CaptureErrorOutput(e.Data);
    }

    private async Task MonitorProcessExitAsync()
    {
        try
        {
            if (_process == null) return;

            await _process.WaitForExitAsync();

            // Flush the remaining output after the process has finished
            await Task.Delay(200);

            var exitCode = _process.ExitCode;

            if (Status != ServerStatus.Stopped && Status != ServerStatus.Error)
            {
                Status = ServerStatus.Stopped;
                OnStatusChanged?.Invoke(Status);
            }

            if (exitCode != 0)
            {
                string errorDetails = "";
                string? classVersionLine = null;
                if (!string.IsNullOrEmpty(_pendingErrorOutput))
                {
                    var lines = _pendingErrorOutput.Trim().Split(Constants.NewLineChars, StringSplitOptions.RemoveEmptyEntries);
                    errorDetails = ExtractErrorBlock(lines, out classVersionLine);
                }

                LastError = errorDetails;

                AppendLog($"[ERROR] ═══════════════════════════════════════");
                AppendLog($"[ERROR] {LocalizationManager.Get("Log_ServerStoppedWithCode", exitCode)}");

                if (classVersionLine != null)
                {
                    // Specific error: Java is too new for older Forge versions
                    AppendLog($"[ERROR] {LocalizationManager.Get("Log_JavaClassFileVersionError", _lastJavaDisplayName ?? "Java", _lastJavaMajorVersion)}");
                }
                else
                {
                    AppendLog($"[ERROR] {LocalizationManager.Get("Log_ServerConfigProblem")}");
                    AppendLog($"[ERROR] {LocalizationManager.Get("Log_MemoryProblem")}");
                }

                AppendLog($"[ERROR] ═══════════════════════════════════════");

                if (exitCode != 0 && Status == ServerStatus.Stopped)
                {
                    Status = ServerStatus.Error;
                    OnStatusChanged?.Invoke(Status);
                }
            }
            else
            {
                AppendLog($" {LocalizationManager.Get("Log_ServerStoppedSuccessfully")}");
            }

            _process?.Dispose();
            _process = null;

            if (Server.Settings.AutoRestart && !_intentionalStop && !_disposed)
            {
                AppendLog($" {LocalizationManager.Get("Log_AutoRestart")} {Server.Settings.AutoRestartDelay} {LocalizationManager.Get("Log_Seconds")}...");
                await Task.Delay(Server.Settings.AutoRestartDelay * Constants.MsPerSecond);

                // The user may have stopped or deleted the server (and disposed this
                // wrapper) while the delay was running. Starting anyway would launch an
                // orphan Java process that nothing tracks or can shut down.
                if (_intentionalStop || _disposed)
                    return;

                Start();
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog($" {LocalizationManager.Get("Log_MonitorCancelled")}");
        }
        catch (Exception ex)
        {
            Logger.Error($"Monitor exit error for {Server.Name}: {ex.Message}", ex, "McServerProcess");
            AppendLog($"[ERROR] {string.Format(LocalizationManager.Get("Log_MonitorError"), ex.Message)}");
        }
    }

    /// <summary>
    /// Builds the error text block for the message box.
    /// 1) class file version — a specific Java error (a single line);
    /// 2) mod incompatibility — the whole block (header + solutions + details) up to the stack trace;
    /// 3) otherwise — the first 2 output lines as before.
    /// </summary>
    private static string ExtractErrorBlock(string[] lines, out string? classVersionLine)
    {
        classVersionLine = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("class file version", StringComparison.OrdinalIgnoreCase))
            {
                classVersionLine = trimmed;
                return trimmed;
            }
        }

        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (ModIncompatibilityParser.IsModIncompatibility(lines[i]))
            {
                start = i;
                break;
            }
        }

        if (start >= 0)
        {
            var block = new StringBuilder();
            for (int i = start; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("at ", StringComparison.Ordinal))
                    break;
                if (block.Length > 0) block.Append('\n');
                block.Append(trimmed);
            }
            return block.ToString().Trim();
        }

        var takeCount = Math.Min(2, lines.Length);
        if (takeCount <= 0)
            return "";

        var sb = new StringBuilder();
        for (int i = 0; i < takeCount; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(lines[i].Trim());
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Strips ANSI escape sequences and control characters from a line
    /// </summary>
    private static string SanitizeOutput(string input) =>
        AnsiRegex().Replace(input, "").Replace("\r", "");

    [GeneratedRegex(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])")]
    private static partial Regex AnsiRegex();

    internal void AppendLog(string line)
    {
        Action<string>? handler;
        string logLine;

        lock (_lock)
        {
            logLine = SanitizeOutput(line);

            _logs.AppendLine(logLine);
            _logLines.Enqueue(logLine);

            if (_logLines.Count > Constants.MaxLogLines)
            {
                _logLines.Dequeue();
                if (_logs.Length > 100_000)
                {
                    var fullText = _logs.ToString();
                    var startIndex = Math.Max(0, fullText.Length - 80_000);
                    _logs.Clear();
                    _logs.Append(fullText[startIndex..]);
                }
            }

            // Captured here, raised below the lock: subscribers touch the UI and may
            // call back into GetLogs()/GetFullLog(), which take the same lock.
            handler = OnLog;
        }

        handler?.Invoke(logLine);
    }

    /// <summary>
    /// Show the server ready message with a delay
    /// </summary>
    private async Task ShowServerReadyDelayedAsync()
    {
        _readyMsgCts?.Cancel();
        _readyMsgCts?.Dispose();
        _readyMsgCts = new CancellationTokenSource();
        var token = _readyMsgCts.Token;

        try
        {
            await Task.Delay(500, token);

            AppendLog($" {LocalizationManager.Get("Log_ServerReady")}");

            if (Status == ServerStatus.Starting)
            {
                Status = ServerStatus.Running;
                OnStatusChanged?.Invoke(Status);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled — a new ready signal is already on its way
        }
    }

    private void ParseOutput(string line)
    {
        if (!_serverReady)
        {
            if (line.Contains("Done ("))
                _serverReady = true;
            else if (line.Contains("Done!"))
                _serverReady = true;
            else if (line.Contains("For help, type"))
                _serverReady = true;
            else if (line.Contains("NEOFORGE") && line.Contains("Loaded"))
                _serverReady = true;

            if (_serverReady)
                _ = ShowServerReadyDelayedAsync();
        }

        if (line.Contains("FAILED TO BIND", StringComparison.OrdinalIgnoreCase))
        {
            AppendLog($"[ERROR] {LocalizationManager.Get("ServerStartError_PortInUse")}");
            LastError = LocalizationManager.Get("ServerStartError_PortInUse");
            Status = ServerStatus.Error;
            OnStatusChanged?.Invoke(Status);
            return;
        }

        var playersMatch = PlayersRegex().Match(line);
        if (playersMatch.Success)
        {
            var newPlayers = int.Parse(playersMatch.Groups[1].Value);
            if (newPlayers != _playersOnline)
            {
                _playersOnline = newPlayers;
                OnPlayersChanged?.Invoke(_playersOnline);
            }
        }
    }

    [GeneratedRegex(@"There are (\d+) players? of a max of \d+")]
    private static partial Regex PlayersRegex();
}
