namespace BitMagic.X16Debugger.Scopes;

internal interface IScopeWrapper
{
    IScopeMap Scope { get; }
    Dictionary<string, object> ObjectTree { get; }
}
