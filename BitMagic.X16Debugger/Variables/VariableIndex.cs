using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using static Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.VariablePresentationHint;

namespace BitMagic.X16Debugger.Variables;

public class VariableIndex : VariableItem
{
    private Func<(string Value, ICollection<Variable> Variables)> GetChildValues { get; }
    private readonly Action<string, string>? _setValue;

    public VariableIndex(string name, Func<(string Value, ICollection<Variable> Variables)> getChildValues, Action<string, string>? setValue = null) : base(name)
    {
        GetChildValues = getChildValues;
        _setValue = setValue;
        GetValue = () => GetChildValues().Value;
        // need to change the constructor here. Need a value, rather than a variable.
        GetExpressionValue = () => GetChildValues().Variables.Select(i => i.Value).ToArray();
        // No setter means VSC should present the variable as read-only, regardless of what the caller passed in.
        Attributes = setValue == null ? AttributesValue.ReadOnly : AttributesValue.None;
        Type = "";
    }

    // value.Name is the child's index/key (as displayed), value.Value is the new value for that child.
    public override void SetVariable(SetVariableArguments value)
    {
        _setValue?.Invoke(value.Name, value.Value);
    }

    public override Variable GetVariable()
    {
        (string Value, ICollection<Variable> Variables) = GetChildValues();

        return new Variable()
        {
            Name = Name,
            Type = Type,
            Value = Value,
            PresentationHint = new VariablePresentationHint() { Kind = Kind, Attributes = Attributes },
            MemoryReference = MemoryReference,
            IndexedVariables = Variables.Count,
            VariablesReference = Id
        };
    }

    public IEnumerable<Variable> GetChildren() => GetChildValues().Variables;
}
