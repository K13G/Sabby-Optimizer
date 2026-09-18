namespace PCTweaker.Core.Services;

public enum UiNotificationKind
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record UiNotification(string Title, string Message, UiNotificationKind Kind = UiNotificationKind.Info);

public static class UiNotificationHub
{
    public static event Action<UiNotification>? Published;

    public static void Publish(string title, string message, UiNotificationKind kind = UiNotificationKind.Info)
    {
        try { Published?.Invoke(new UiNotification(title, message, kind)); }
        catch { }
    }
}
