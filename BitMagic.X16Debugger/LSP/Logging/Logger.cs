using Microsoft.Extensions.Logging;

namespace BitMagic.X16Debugger.LSP.Logging;

public class Logger : ILogger
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

}
