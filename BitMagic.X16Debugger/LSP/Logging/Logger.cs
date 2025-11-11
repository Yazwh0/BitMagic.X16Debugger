using BitMagic.Common;
using Microsoft.Extensions.Logging;

namespace BitMagic.X16Debugger.LSP.Logging;

public class Logger : ILogger, IEmulatorLogger
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return new NoopDisposable();
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Console.WriteLine(formatter(state, exception));
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private IEmulatorLogger? _secondaryLogger;

    public void AddSecondaryLogger(IEmulatorLogger? secondaryLogger)
    {
        _secondaryLogger = secondaryLogger;
    }

    public void Log(string message)
    {
        Console.Write(message);

        if (_secondaryLogger != null) _secondaryLogger.Log(message);
    }

    public void LogError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();

        if (_secondaryLogger != null) _secondaryLogger.LogError(message);
    }

    public void LogError(string message, ISourceFile source, int lineNumber)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();

        if (_secondaryLogger != null) _secondaryLogger.LogError(message, source, lineNumber);
    }

    public void LogLine(string message)
    {
        Console.WriteLine(message);

        if (_secondaryLogger != null) _secondaryLogger.LogLine(message);
    }
}
