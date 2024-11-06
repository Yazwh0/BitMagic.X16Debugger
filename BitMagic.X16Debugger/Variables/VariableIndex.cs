using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using static Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.VariablePresentationHint;

namespace BitMagic.X16Debugger.Variables;

public class VariableIndex : VariableItem
{
    private Func<(string Value, ICollection<Variable> Variables)> GetChildValues { get; }

    public VariableIndex(string name, Func<(string Value, ICollection<Variable> Variables)> getChildValues) : base(name)
    {
        GetChildValues = getChildValues;
        GetValue = () => GetChildValues().Value;
        // need to change the constructor here. Need a value, rather than a variable.
        GetExpressionValue = () => GetChildValues().Variables.Select(i => i.Value).ToArray();
        Attributes = AttributesValue.ReadOnly;
        Type = "";
    }

    public override void SetVariable(SetVariableArguments value)
    {
        // not sure what to do here.
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
