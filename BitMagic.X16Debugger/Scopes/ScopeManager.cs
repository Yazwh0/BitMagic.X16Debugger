using Scope = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Scope;

namespace BitMagic.X16Debugger.Scopes;

internal class ScopeManager
{
    private Dictionary<string, IScopeMap> Scopes { get; } = new Dictionary<string, IScopeMap>();
    private Dictionary<int, IScopeMap> ScopesById { get; } = new Dictionary<int, IScopeMap>();

    private readonly IdManager _idManager;

    public DebuggerLocalVariables? LocalScope { get; internal set; } = null;

    public ScopeManager(IdManager idManager)
    {
        _idManager = idManager;
    }

    public ScopeMap GetScope(string name, bool expensive)
    {
        if (Scopes.ContainsKey(name))
            return Scopes[name] as ScopeMap ?? throw new Exception($"{name} is not a ScopeMap");

        var toReturn = new ScopeMap(name, expensive, _idManager.GetId());
        Scopes.Add(name, toReturn);
        ScopesById.Add(toReturn.Id, toReturn);

        return toReturn;
    }

    public IScopeMap? GetScope(int id)
    {
        if (ScopesById.ContainsKey(id))
            return ScopesById[id];

        return null;
    }

    public IScopeMap CreateLocalsScope(string name)
    {
        LocalScope = new DebuggerLocalVariables(name, _idManager.GetId());
        if (Scopes.ContainsKey(name))
        {
            Scopes.Remove(name);
        }
        Scopes.Add(name, LocalScope);
        ScopesById.Add(LocalScope.Id, LocalScope);
        return LocalScope;
    }

    public IEnumerable<Scope> AllScopes => Scopes.Values.Select(i => i.Scope);
}
