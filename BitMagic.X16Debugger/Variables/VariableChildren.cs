using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using static Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.VariablePresentationHint;

namespace BitMagic.X16Debugger.Variables;

public class VariableChildren : VariableItem
{
    private readonly Dictionary<string, IVariableItem> _children;

    public VariableChildren(string name, Func<string> getValue, string variableType, IVariableItem[] children) : base(name, getValue)
    {
        _children = children.ToDictionary(i => i.Name, i => i);
        GetExpressionValue = () => _children.ToDictionary(i => i.Key, i => (object?)i.Value.GetExpressionValue);
        Attributes = AttributesValue.ReadOnly;
        Type = variableType;
    }

    public VariableChildren(string name, Func<string> getValue, IVariableItem[] children) : this(name, getValue, "", children)
    {
    }

    public override void SetVariable(SetVariableArguments value)
    {
        if (!_children.ContainsKey(value.Name))
            return; // throw error?

        var child = _children[value.Name];
        if (child.SetValue != null)
            child.SetValue(value.Value);
    }

    public override Variable GetVariable() => new Variable()
    {
        Name = Name,
        Type = Type,
        Value = GetValue == null ? "" : ExpressionManager.Stringify(GetValue()),
        PresentationHint = new VariablePresentationHint() { Kind = Kind, Attributes = Attributes },
        MemoryReference = MemoryReference,
        NamedVariables = _children.Count,
        VariablesReference = Id
    };

    public IEnumerable<IVariableItem> Children => _children.Values;
}
