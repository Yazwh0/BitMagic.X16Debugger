using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BitMagic.X16Debugger;

internal class ScopeManager
{
    private Dictionary<string, ScopeMap> Scopes { get; } = new Dictionary<string, ScopeMap>();
    private Dictionary<int, ScopeMap> ScopesById { get; } = new Dictionary<int, ScopeMap>();

    private readonly IdManager _idManager;

    public ScopeManager(IdManager idManager)
    {
        _idManager = idManager;
    }

    public ScopeMap GetScope(string name, bool expensive)
    {
        if (Scopes.ContainsKey(name))
            return Scopes[name];

        var toReturn = new ScopeMap(name, expensive, _idManager.GetId());
        Scopes.Add(name, toReturn);
        ScopesById.Add(toReturn.Id, toReturn);

        return toReturn;
    }

    public ScopeMap? GetScope(int id)
    {
        if (ScopesById.ContainsKey(id))
            return ScopesById[id];

        return null;
    }

    public IEnumerable<Scope> AllScopes => Scopes.Values.Select(i => i.Scope);
}

internal class ScopeMap
{
    //private static int _scopeId = 1;
    public Scope Scope { get; }
    public int Id => Scope.VariablesReference;

    private List<IVariableMap> _variables { get; } = new List<IVariableMap>();
    public ReadOnlyCollection<IVariableMap> Variables => _variables.AsReadOnly();

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

    public void AddVariable(IVariableMap variable)
    {
        _variables.Add(variable);
        Scope.NamedVariables = _variables.Count;
    }
}
