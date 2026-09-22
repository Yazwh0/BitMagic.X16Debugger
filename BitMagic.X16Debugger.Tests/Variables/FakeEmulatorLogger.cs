using BitMagic.Common;

namespace BitMagic.X16Debugger.Tests.Variables;

// Minimal IEmulatorLogger double: real implementations forward to a DebugAdapterBase protocol
// connection, which is unnecessary weight for wiring up a VariableManager in tests.
internal class FakeEmulatorLogger : IEmulatorLogger
{
    public void Log(string message) { }
    public void LogLine(string message) { }
    public void LogError(string message) { }
    public void LogError(string message, ISourceFile source, int lineNumber) { }
}
