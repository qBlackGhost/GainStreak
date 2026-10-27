using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using GymApp.Services;

namespace GymApp.App;

/// <summary>
/// The safety net for errors nothing else caught: each is written to errors.log and the user sees a short
/// "Something went wrong" banner instead of a crash or raw exception text. Screens still catch what they expect
/// themselves, with their own messages; this is only for what slips through.
/// </summary>
public static class ErrorHandling
{
    public const string UserMessage = "Something went wrong. The details were saved: Feedback → Report a problem attaches them.";

    /// <summary>Hooks the three places an unhandled error can surface. <paramref name="show"/> runs on the UI thread.</summary>
    public static void Install(ErrorLog log, Action<string> show)
    {
        void Report(Exception e, string source)
        {
            log.Write(e, source);
            Dispatcher.UIThread.Post(() => show(UserMessage));
        }

        // Errors thrown by UI-thread code (commands, event handlers): mark handled so the app keeps running
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            Report(e.Exception, "UI thread");
        };

        // Fire-and-forget tasks nobody awaited
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            Report(e.Exception, "Unobserved task");
        };

        // Anything else (another thread): the runtime ends the process after this, so at least the log has it
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) log.Write(ex, "Unhandled (app closing)");
        };
    }
}
