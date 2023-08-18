using BitMagic.Common;
using BitMagic.Compiler.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using System.Text;

namespace BitMagic.X16Debugger;

public class DebugLogger : IEmulatorLogger
{

    private readonly DebugAdapterBase _adaptor;
    private readonly StringBuilder _line = new StringBuilder();

    public DebugLogger(DebugAdapterBase adaptor)
    {
        _adaptor = adaptor;
    }

    public void LogLine(string message)
    {
        _adaptor.Protocol.SendEvent(new OutputEvent() { Output = _line.ToString() + message + Environment.NewLine });
        _line.Clear();
    }

    public void Log(string message) => _line.Append(message);

    public void LogError(string message) =>    
        _adaptor.Protocol.SendEvent(new OutputEvent() {
            Output = message + Environment.NewLine,
            Severity = OutputEvent.SeverityValue.Error,
            Category = OutputEvent.CategoryValue.Stderr
        });
    
    public void LogError(string message, ISourceFile source, int lineNumber) =>
        _adaptor.Protocol.SendEvent(new OutputEvent()
        {
            Output = message + Environment.NewLine,
            Severity = OutputEvent.SeverityValue.Error,
            Category = OutputEvent.CategoryValue.Stderr,
            Line = lineNumber,
            Source = source.AsSource()
        });
}
