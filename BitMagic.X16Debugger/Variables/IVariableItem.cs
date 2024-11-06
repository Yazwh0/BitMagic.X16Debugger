using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.Variables;

/// <summary>
/// Wraps the local scope map, which provides the local variables from the source map
/// </summary>
//internal class LocalScopeWrapper : IScopeWrapper
//{
//    public IScopeMap Scope { get; set; }
//    public Dictionary<string, object> ObjectTree => Scope.Variables.ToDictionary(i => i.Name, i => (object)i.GetVariable().Value);

//    public LocalScopeWrapper(IScopeMap scope)
//    {
//        Scope = scope;
//    }
//}

public interface IVariableItem
{
    int Id { get; }
    string Name { get; }
    Variable GetVariable(); // Get the Variable from a variable request
    Func<object>? GetValue { get; } // Get value for Variables
    Action<string>? SetValue { get; } // Set value from a call to SetVariable
    void SetVariable(SetVariableArguments value); // From a variable edit
    Func<object>? GetExpressionValue { get; }  // used by the Watches
}
