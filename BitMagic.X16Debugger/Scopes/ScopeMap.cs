using BitMagic.X16Debugger.Variables;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.Scopes;

internal class ScopeMap : IScopeMap
{
    public Scope Scope { get; }
    public int Id => Scope.VariablesReference;

    private List<IVariableItem> _variables { get; } = new List<IVariableItem>();
    public IEnumerable<IVariableItem> Variables => _variables;

    public ScopeMap(string name, bool expensive, int id)
    {
        Scope = new Scope
        {
            Name = name,
            Expensive = expensive,
            VariablesReference = id,
            PresentationHint = Scope.PresentationHintValue.Locals
        };
    }

    public ScopeMap(string name, bool expensive, bool registers, int id)
    {
        Scope = new Scope
        {
            Name = name,
            Expensive = expensive,
            VariablesReference = id,
            PresentationHint = Scope.PresentationHintValue.Registers
        };
    }

    public void AddVariable(IVariableItem variable)
    {
        _variables.Add(variable);
        Scope.NamedVariables = _variables.Count;
    }

    public void Clear()
    {
        _variables.Clear();
    }
}
