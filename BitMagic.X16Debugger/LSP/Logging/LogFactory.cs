using Microsoft.Extensions.Logging;

namespace BitMagic.X16Debugger.LSP.Logging;

public sealed class LogFactory : ILoggerFactory
{
    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger();
    }

    public void Dispose()
    {
    }
}
