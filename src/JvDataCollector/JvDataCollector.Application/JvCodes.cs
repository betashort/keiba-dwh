namespace JvDataCollector;

public static class JvCodes
{
    public static readonly int[] BackoffMinutes = [1, 5, 15, 60];

    public const long MinimumFreeBytes = 5L * 1024 * 1024 * 1024;

    public static bool IsNonRetry(int code) => code is
        -111 or -112 or -113 or -114 or -115 or -116 or
        -201 or -211 or
        -301 or -302 or -303 or -305;

    public static bool IsBackoff(int code) => code is
        -411 or -412 or -413 or -421 or -431 or -502 or -503 or -504;

    public static bool BlocksSchedule(int code) => code is -201 or -211 or -301 or -302 or -303 or -305;

    public static AttentionReason ReasonFor(int code) => code switch
    {
        -305 => AttentionReason.Agreement,
        -301 or -302 or -303 => AttentionReason.ServiceKey,
        -211 => AttentionReason.SavePath,
        _ => AttentionReason.JvRejected,
    };
}

public enum RetryAction
{
    None,
    Backoff,
    SuspendToday,
    Attention,
    RejectOption1,
}

public readonly record struct RetryDecision(RetryAction Action, TimeSpan Delay, AttentionReason? Reason);

public static class RetryPolicy
{
    public static RetryDecision For(int code, int consecutiveFailures, string dataspec, int option)
    {
        if (option == 1 && code == -116 && dataspec is "COMM" or "MING")
        {
            return new RetryDecision(RetryAction.RejectOption1, default, null);
        }

        if (option == 0 && code is -111 or -112 or -113 or -114 or -115 or -116)
        {
            return new RetryDecision(RetryAction.None, default, null);
        }

        if (JvCodes.IsNonRetry(code))
        {
            return new RetryDecision(RetryAction.Attention, default, JvCodes.ReasonFor(code));
        }

        var next = consecutiveFailures + 1;
        if (next >= 6)
        {
            return new RetryDecision(RetryAction.SuspendToday, default, null);
        }

        var index = Math.Min(next - 1, JvCodes.BackoffMinutes.Length - 1);
        return new RetryDecision(RetryAction.Backoff, TimeSpan.FromMinutes(JvCodes.BackoffMinutes[index]), null);
    }
}
