namespace PCTweaker.Core.Services;

public interface IAppLogger
{
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
    void Critical(string message, Exception? exception = null);
}
