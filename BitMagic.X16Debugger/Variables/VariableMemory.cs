using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.Variables;

// Doesn't actually return the memory values.
public class VariableMemory : VariableItem
{
    public VariableMemory(string name, Func<string> getValue, string memoryReference, Func<byte[]> getValues) : base(name, getValue)
    {
        MemoryReference = memoryReference;
        Type = "";
        GetExpressionValue = () => new MemoryWrapper(getValues);
    }

    // shouldn't get called.
    public override void SetVariable(SetVariableArguments value)
    {
    }
}
