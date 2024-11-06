namespace BitMagic.X16Debugger.Variables;

internal interface IScopeWrapper
{
    IScopeMap Scope { get; }
    Dictionary<string, object> ObjectTree { get; }
}
