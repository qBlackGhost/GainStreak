using System;
using System.IO;
using System.Text;

namespace GymApp.Services;

/// <summary>
/// Writes unexpected errors to errors.log in the app's data folder, so a crash report has the details while the
/// user only sees "Something went wrong". Past <see cref="MaxBytes"/> the log moves to errors.old.log (replacing
/// the previous one) and starts again. Never throws: a log that can't be written is just skipped.
/// Testers send it with Profile's "Copy error report" (<see cref="BuildReport"/>); nothing is uploaded on its own.
/// </summary>
/// <param name="appInfo">"GainStreak 0.15.0 on macOS 15.1 (Arm64)": on every entry, since a log can outlive an update.</param>
public class ErrorLog(string directory, long maxBytes = ErrorLog.DefaultMaxBytes, string? appInfo = null)
{
    public const long DefaultMaxBytes = 1_000_000;
    public const string FileName = "errors.log";
    public const string OldFileName = "errors.old.log";

    /// <summary>The most of the logs a report carries, newest kept: enough for a message, small enough to paste.</summary>
    public const int MaxReportChars = 100_000;
    public const string NoErrorsText = "No errors recorded.";

    private readonly object _lock = new();

    public long MaxBytes { get; } = maxBytes;
    public string? AppInfo { get; } = appInfo;
    public string FilePath => Path.Combine(directory, FileName);
    public string OldFilePath => Path.Combine(directory, OldFileName);

    /// <summary>Appends one entry: the time, where it came from, and the full exception. False if it couldn't be written.</summary>
    public bool Write(Exception error, string source, DateTime? now = null)
    {
        try
        {
            var entry = new StringBuilder()
                .Append('[').Append((now ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
                .Append(source).AppendLine(AppInfo is null ? "" : $" ({AppInfo})")
                .AppendLine(error.ToString())
                .AppendLine()
                .ToString();

            lock (_lock)
            {
                Directory.CreateDirectory(directory);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length + entry.Length > MaxBytes)
                    File.Move(FilePath, OldFilePath, overwrite: true);
                File.AppendAllText(FilePath, entry);
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The text "Copy error report" puts on the clipboard: the app and system, then both logs, oldest
    /// first, cut to the newest <see cref="MaxReportChars"/>. Never throws.</summary>
    public string BuildReport(DateTime? now = null)
    {
        string logs;
        lock (_lock)
            logs = Read(OldFilePath) + Read(FilePath);
        if (logs.Length > MaxReportChars)
            logs = "[older entries cut]" + Environment.NewLine + logs[^MaxReportChars..];

        return new StringBuilder()
            .AppendLine("GainStreak error report")
            .Append("Created: ").AppendLine((now ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss"))
            .Append("App: ").AppendLine(AppInfo ?? "unknown")
            .AppendLine()
            .Append(string.IsNullOrWhiteSpace(logs) ? NoErrorsText + Environment.NewLine : logs)
            .ToString();
    }

    private static string Read(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : ""; }
        catch (Exception) { return ""; }
    }
}
