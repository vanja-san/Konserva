using Konserva.Localization;
using Konserva.Models;
using Konserva.Utilities;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Konserva.Services;

/// <summary>
/// Обработка вывода, парсинг логов, мониторинг завершения процесса
/// </summary>
public partial class McServerProcess
{
    private void OnOutput(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
            return;

        if (e.Data.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            e.Data.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
            e.Data.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            _pendingErrorOutput ??= "";
            _pendingErrorOutput += e.Data + "\n";
        }

        AppendLog(e.Data);
        ParseOutput(e.Data);
    }

    private void OnError(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
            return;

        var errorLine = $"[STDERR] {e.Data}";
        AppendLog(errorLine);
        _pendingErrorOutput ??= "";
        _pendingErrorOutput += e.Data + "\n";
    }

    private async Task MonitorProcessExitAsync()
    {
        try
        {
            if (_process == null) return;

            await _process.WaitForExitAsync();

            // Дочитываем оставшийся вывод после завершения процесса
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
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.Contains("class file version", StringComparison.OrdinalIgnoreCase))
                        {
                            classVersionLine = trimmed;
                            break;
                        }
                    }

                    if (classVersionLine != null)
                        errorDetails = classVersionLine;
                    else
                    {
                        var takeCount = Math.Min(2, lines.Length);
                        if (takeCount > 0)
                        {
                            var sb = new StringBuilder();
                            for (int i = 0; i < takeCount; i++)
                            {
                                if (i > 0) sb.Append('\n');
                                sb.Append(lines[i].Trim());
                            }
                            errorDetails = sb.ToString().Trim();
                        }
                    }
                }

                LastError = errorDetails;

                AppendLog($"[ERROR] ═══════════════════════════════════════");
                AppendLog($"[ERROR] {LocalizationManager.Get("Log_ServerStoppedWithCode", exitCode)}");

                if (classVersionLine != null)
                {
                    // Специфичная ошибка: слишком новая Java для старых версий Forge
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

            if (Server.Settings.AutoRestart && !_intentionalStop)
            {
                AppendLog($" {LocalizationManager.Get("Log_AutoRestart")} {Server.Settings.AutoRestartDelay} {LocalizationManager.Get("Log_Seconds")}...");
                await Task.Delay(Server.Settings.AutoRestartDelay * Constants.MsPerSecond);
                if (!_intentionalStop)
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
    /// Удаляет ANSI escape-последовательности и управляющие символы из строки
    /// </summary>
    private static string SanitizeOutput(string input) =>
        AnsiRegex().Replace(input, "").Replace("\r", "");

    [GeneratedRegex(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])")]
    private static partial Regex AnsiRegex();

    internal void AppendLog(string line)
    {
        lock (_lock)
        {
            var logLine = SanitizeOutput(line);

            _logs.AppendLine(logLine);
            _logLines.Add(logLine);

            if (_logLines.Count > Constants.MaxLogLines)
            {
                _logLines.RemoveAt(0);
                if (_logs.Length > 100_000)
                {
                    var fullText = _logs.ToString();
                    var startIndex = Math.Max(0, fullText.Length - 80_000);
                    _logs.Clear();
                    _logs.Append(fullText[startIndex..]);
                }
            }

            OnLog?.Invoke(logLine);
        }
    }

    /// <summary>
    /// Показать сообщение о готовности сервера с задержкой
    /// </summary>
    private async Task ShowServerReadyDelayedAsync()
    {
        _readyMsgCts?.Cancel();
        _readyMsgCts = new CancellationTokenSource();
        var token = _readyMsgCts.Token;

        try
        {
            await Task.Delay(500, token);

            AppendLog($" {LocalizationManager.Get("Log_ServerReady")}");
            AppendLog($" {LocalizationManager.Get("Log_ServerReady_Commands")}");

            if (Status == ServerStatus.Starting)
            {
                Status = ServerStatus.Running;
                OnStatusChanged?.Invoke(Status);
            }
        }
        catch (OperationCanceledException)
        {
            // Отменено — новый сигнал готовности уже в пути
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
