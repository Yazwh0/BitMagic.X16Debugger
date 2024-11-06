using BitMagic.X16Debugger.Variables;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.Scopes;

internal interface IScopeMap
{
    IEnumerable<IVariableItem> Variables { get; }
    int Id { get; }
    Scope Scope { get; }
}
