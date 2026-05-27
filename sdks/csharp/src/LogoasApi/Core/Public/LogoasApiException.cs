namespace LogoasApi;

/// <summary>
/// Base exception class for all exceptions thrown by the SDK.
/// </summary>
public class LogoasApiException(string message, Exception? innerException = null)
    : Exception(message, innerException);
