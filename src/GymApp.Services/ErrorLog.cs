using System;
using System.IO;
using System.Text;

namespace GymApp.Services;

/// <summary>
/// Writes unexpected errors to errors.log in the app's data folder, so a crash report has the details while the
/// user only sees "Something went wrong". Past <see cref="MaxBytes"/> the log moves to errors.old.log (replacing
/// the previous one) and starts again. Never throws: a log that can't be written is just skipped.
/// </summary>
public class ErrorLog(string directory, long maxBytes = ErrorLog.DefaultMaxBytes)
{
    public const long DefaultMaxBytes = 1_000_000;
    public const string FileName = "errors.log";
    public const string OldFileName = "errors.old.log";

    private readonly object _lock = new();

    public long MaxBytes { get; } = maxBytes;
    public string FilePath => Path.Combine(directory, FileName);
    public string OldFilePath => Path.Combine(directory, OldFileName);

    /// <summary>Appends one entry: the time, where it came from, and the full exception. False if it couldn't be written.</summary>
    public bool Write(Exception error, string source, DateTime? now = null)
    {
        try
        {
            var entry = new StringBuilder()
                .Append('[').Append((now ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
                .AppendLine(source)
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
}
