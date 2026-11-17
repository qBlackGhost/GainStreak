using Android.Runtime;
using Kotlin.Coroutines;
using Kotlin.Coroutines.Intrinsics;

namespace GymApp.Android;

/// <summary>
/// Calls a Kotlin <c>suspend</c> function from C# (Health Connect, Phase 23): the binding gives each one a last
/// <see cref="IContinuation"/> parameter. The call returns its result at once, or <c>COROUTINE_SUSPENDED</c> and later
/// resumes the continuation (on any thread). A failure arrives as a <c>kotlin.Result$Failure</c>, which isn't bound:
/// its <c>exception</c> field is read through Java reflection and thrown (23a results).
/// </summary>
internal sealed class KotlinSuspend : Java.Lang.Object, IContinuation
{
    private readonly TaskCompletionSource<Java.Lang.Object?> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ICoroutineContext Context => EmptyCoroutineContext.Instance!;

    public void ResumeWith(Java.Lang.Object result)
    {
        if (result?.Class.Name == "kotlin.Result$Failure")
        {
            var field = result.Class.GetDeclaredField("exception");
            field.Accessible = true;
            _done.TrySetException(field.Get(result)?.JavaCast<Java.Lang.Throwable>() ?? new Java.Lang.Throwable("Health Connect call failed"));
        }
        else _done.TrySetResult(result);
    }

    /// <summary>Runs <paramref name="call"/> and returns its result cast to <typeparamref name="T"/> (null for Unit).</summary>
    public static async Task<T?> Run<T>(Func<IContinuation, Java.Lang.Object?> call) where T : Java.Lang.Object
    {
        var continuation = new KotlinSuspend();
        var result = call(continuation);
        if (result is not null && result.Equals(IntrinsicsKt.COROUTINE_SUSPENDED)) result = await continuation._done.Task;
        return result is null or Kotlin.Unit ? null : result.JavaCast<T>();
    }

    /// <summary>For calls whose result isn't needed (deletes).</summary>
    public static Task Run(Func<IContinuation, Java.Lang.Object?> call) => Run<Java.Lang.Object>(call);
}
