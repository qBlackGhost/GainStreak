using System.Diagnostics;
using System.Text;

namespace MobileSpike;

/// <summary>Timestamps since the process started, to the console (logcat on Android, the simctl console on iOS) and the screen.
/// Every line starts with "SPIKE" so it can be grepped out of the device log.</summary>
public static class SpikeLog
{
    private static readonly StringBuilder Lines = new();
    private static readonly Lock Gate = new();

    /// <summary>When the OS started the process, so the times include the runtime's own start. Null where the platform
    /// doesn't say (then times count from the first managed line).</summary>
    public static readonly DateTime? ProcessStart = TryProcessStart();
    private static readonly DateTime FirstManaged = DateTime.Now;

    public static string Text { get { lock (Gate) return Lines.ToString(); } }

    public static double SinceStartMs => (DateTime.Now - (ProcessStart ?? FirstManaged)).TotalMilliseconds;

    public static void Mark(string what) => Write($"[{SinceStartMs,7:0} ms] {what}");

    public static void FirstFrame() =>
        Write($"[{SinceStartMs,7:0} ms] FIRST FRAME (from {(ProcessStart is null ? "first managed code" : "process start")})");

    public static void Write(string line)
    {
        lock (Gate) Lines.AppendLine(line);
        Console.WriteLine("SPIKE " + line);
    }

    private static DateTime? TryProcessStart()
    {
        try { return Process.GetCurrentProcess().StartTime; }
        catch { return null; }
    }
}
