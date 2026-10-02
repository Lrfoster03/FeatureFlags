namespace FeatureFlags.Services;

internal static class InvitationDiagnostics
{
    // Exception messages, Data and ToString can contain tokens, SMTP replies or credentials.
    // Keep diagnostic types, SQL state and code locations, never payloads or exception objects.
    internal static void LogUnexpected(ILogger logger, string operation, Exception error)
    {
        var cause = error.GetBaseException();
        logger.LogError("Invitation {Operation} failed: {ErrorType}; cause {CauseType}; SQL state {SqlState}; at {StackTrace}",
            operation, error.GetType().FullName, cause.GetType().FullName,
            (cause as Npgsql.PostgresException)?.SqlState, error.StackTrace);
    }
}
