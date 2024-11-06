namespace BitMagic.X16Debugger.Variables;

internal class ScopeWrapper : IScopeWrapper
{
    private readonly ScopeMap _scope;
    public IScopeMap Scope => _scope;
    public Dictionary<string, object> ObjectTree { get; set; } = new();

    public ScopeWrapper(ScopeMap scope)
    {
        _scope = scope;
    }

    public void AddVariable(IVariableItem variable)
    {
        _scope.AddVariable(variable);

        if (variable.GetExpressionValue != null)
            ObjectTree.Add(variable.Name, variable.GetExpressionValue);
    }

    public void Clear()
    {
        _scope.Clear();
        ObjectTree.Clear();
    }
}
