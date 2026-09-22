using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using static Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.VariablePresentationHint;

namespace BitMagic.X16Debugger.Variables;

internal class VariableMap : IVariableItem
{
    private readonly Variable _variable;
    private readonly Action<string>? _setValue;
    public string Name => _variable.Name;
    public int Id => 0;

    public Func<object> GetValue { get; }

    public Action<string>? SetValue => _setValue;

    public Func<object>? GetExpressionValue { get; }

    public VariableMap(string name, string type, Func<object> getFunction,
        KindValue kindValue = KindValue.Property,
        AttributesValue attribute = AttributesValue.None,
        Action<string>? setValue = null
        ) : this(name, type, getFunction, getFunction, kindValue, attribute, setValue)
    {
    }

    public VariableMap(string name, string type, Func<object> getFunction, Func<object> getExpression,
        KindValue kindValue = KindValue.Property,
        AttributesValue attribute = AttributesValue.None,
        Action<string>? setValue = null
    )
    {
        _setValue = setValue;

        // No setter means VSC should present the variable as read-only, regardless of what the caller passed in.
        if (setValue == null)
            attribute |= AttributesValue.ReadOnly;

        _variable = new Variable()
        {
            Name = name,
            Type = type,
            PresentationHint = new VariablePresentationHint() { Kind = kindValue, Attributes = attribute }
        };

        GetValue = getFunction;
        GetExpressionValue = getExpression;
    }

    public Variable GetVariable()
    {
        try
        {
            _variable.Value = ExpressionManager.Stringify(GetValue());
        }
        catch (Exception e)
        {
            _variable.Value = $"Error: {e.Message}";
        }
        return _variable;
    }

    public void SetVariable(SetVariableArguments value)
    {
        _setValue?.Invoke(value.Value);
    }
}