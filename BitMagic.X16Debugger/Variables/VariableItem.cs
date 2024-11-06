using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using static Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.VariablePresentationHint;

namespace BitMagic.X16Debugger.Variables;

public abstract class VariableItem : IVariableItem
{
    public int Id { get; set; }
    public string Name { get; }
    public Func<object>? GetExpressionValue { get; protected set; }

    internal string Type { get; set; } = "";
    internal KindValue Kind { get; set; }
    internal AttributesValue Attributes { get; set; }
    internal string? MemoryReference { get; set; }

    public Func<object>? GetValue { get; protected set; }
    public Action<string>? SetValue { get; protected set; }
    public abstract void SetVariable(SetVariableArguments value);

    protected VariableItem(string name)
    {
        Name = name;
    }

    protected VariableItem(string name, Func<string> getValue)
    {
        Name = name;
        GetValue = getValue;
        GetExpressionValue = getValue;
    }

    protected VariableItem(string name, Func<string> getValue, Action<string> setValue)
    {
        Name = name;
        GetValue = getValue;
        GetExpressionValue = getValue;
        SetValue = setValue;
    }

    public virtual Variable GetVariable() => new Variable()
    {
        Name = Name,
        Type = Type,
        Value = GetValue == null ? "" : ExpressionManager.Stringify(GetValue()),
        PresentationHint = new VariablePresentationHint() { Kind = Kind, Attributes = Attributes },
        MemoryReference = MemoryReference
    };
}
