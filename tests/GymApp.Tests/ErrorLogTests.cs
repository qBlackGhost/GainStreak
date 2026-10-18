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
