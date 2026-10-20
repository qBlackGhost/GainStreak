using GymApp.Services;

namespace GymApp.Tests;

public class ErrorLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gymapp-errorlog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static Exception Thrown(string message)
    {
        try { throw new InvalidOperationException(message); }
        catch (Exception e) { return e; }
    }

    [Fact] public void Write_CreatesTheFolder_AndAppendsTheFullException()
    {
        var log = new ErrorLog(_dir);
        Assert.True(log.Write(Thrown("first"), "UI thread", new DateTime(2026, 10, 18, 9, 30, 0)));
        Assert.True(log.Write(Thrown("second"), "Unobserved task"));

        var text = File.ReadAllText(log.FilePath);
        Assert.StartsWith("[2026-10-18 09:30:00] UI thread", text);
        Assert.Contains("System.InvalidOperationException: first", text);
        Assert.Contains("at GymApp.Tests.ErrorLogTests.Thrown", text);   // the stack trace is kept
        Assert.Contains("Unobserved task", text);
        Assert.Contains("second", text);
    }

    [Fact] public void Write_RollsOverPastTheCap()
    {
        var log = new ErrorLog(_dir, maxBytes: 2_000);
        for (var i = 0; i < 20; i++) log.Write(Thrown($"error {i}"), "test");

        Assert.True(File.Exists(log.OldFilePath));
        Assert.True(new FileInfo(log.FilePath).Length <= 2_000);
        Assert.Contains("error 19", File.ReadAllText(log.FilePath));
    }

    [Fact] public void Write_StampsTheAppOnEveryEntry()
    {
        var log = new ErrorLog(_dir, appInfo: "GainStreak 0.15.0 on Darwin (Arm64)");
        log.Write(Thrown("x"), "UI thread", new DateTime(2026, 10, 18, 9, 30, 0));

        Assert.StartsWith("[2026-10-18 09:30:00] UI thread (GainStreak 0.15.0 on Darwin (Arm64))", File.ReadAllText(log.FilePath));
    }

    [Fact] public void Report_HasTheAppAndBothLogs_OldestFirst()
    {
        var log = new ErrorLog(_dir, maxBytes: 2_000, appInfo: "GainStreak 0.15.0");
        for (var i = 0; i < 20; i++) log.Write(Thrown($"error {i}"), "test");

        var report = log.BuildReport(new DateTime(2026, 10, 20, 12, 0, 0));

        Assert.StartsWith("GainStreak error report", report);
        Assert.Contains("Created: 2026-10-20 12:00:00", report);
        Assert.Contains("App: GainStreak 0.15.0", report);
        var older = report.IndexOf(File.ReadAllText(log.OldFilePath).Trim(), StringComparison.Ordinal);
        var newer = report.IndexOf("error 19", StringComparison.Ordinal);
        Assert.True(older > 0 && newer > older);
    }

    [Fact] public void Report_WithNoLog_SaysSo()
    {
        var report = new ErrorLog(_dir).BuildReport();
        Assert.Contains(ErrorLog.NoErrorsText, report);
        Assert.Contains("App: unknown", report);
    }

    [Fact] public void Report_KeepsOnlyTheNewestPart_OfABigLog()
    {
        var log = new ErrorLog(_dir, maxBytes: 10_000_000);
        for (var i = 0; i < 400; i++) log.Write(Thrown($"error {i} " + new string('x', 400)), "test");

        var report = log.BuildReport();

        Assert.True(report.Length < ErrorLog.MaxReportChars + 1_000);
        Assert.Contains("[older entries cut]", report);
        Assert.Contains("error 399", report);
        Assert.DoesNotContain("error 0 ", report);
    }

    [Fact] public void Write_NeverThrows_WhenTheFolderCantBeWritten()
    {
        File.WriteAllText(_dir, "a file where the folder should be");   // so the folder can't be created
        try
        {
            Assert.False(new ErrorLog(Path.Combine(_dir, "sub")).Write(Thrown("x"), "test"));
        }
        finally
        {
            File.Delete(_dir);
        }
    }
}
