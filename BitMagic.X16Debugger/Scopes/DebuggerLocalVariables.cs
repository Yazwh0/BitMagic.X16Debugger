using BitMagic.Common;
using BitMagic.X16Debugger.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.Scopes;

internal class DebuggerLocalVariables : IScopeMap
{
    private readonly List<IVariableItem> _variables = new();
    public IEnumerable<IVariableItem> Variables => _variables;
    public Scope Scope { get; }
    public int Id { get; }
    internal DebuggerLocalVariables(string name, int id)
    {
        Id = id;
        Scope = new Scope
        {
            Name = name,
            Expensive = false,
            VariablesReference = id,
            PresentationHint = Scope.PresentationHintValue.Locals
        };
    }

    public void SetLocalScope(StackFrameState? state, Emulator emulator, ExpressionManager expressionManager)
    {
        _variables.Clear();
        if (state == null || state.Scope == null)
            return;

        var memory = new MemoryWrapper(() => emulator.Memory.ToArray());

        // find first non-anon procecure
        var s = state.Scope;
        var done = new HashSet<string>();

        while (true)
        {
            foreach (var i in s.Variables.Values)
            {
                if (i.Value.VariableDataType is VariableDataType.Constant or VariableDataType.ProcStart or VariableDataType.ProcEnd or VariableDataType.SegmentStart or VariableDataType.LabelPointer)
                    continue;

                if (done.Contains(i.Key))
                    continue;

                done.Add(i.Key);

                var j = i;

                if (i.Value.VariableType == VariableType.DebuggerExpression)
                {
                    var debugVar = j.Value as DebuggerVariable ?? throw new Exception("Variable claims to be a debugger expression but isnt.");

                    Func<string> getter = debugVar.ToExpressionFunction(expressionManager);

                    var type = j.Value.VariableTypeText();

                    _variables.Add(new VariableMap(i.Value.Name, type, getter));
                }
                else
                {
                    // todo: handle arrays
                    Func<string> getter = j.Value.ToStringFunction(memory);

                    var type = j.Value.VariableTypeText();

                    if (j.Value.Value < 256)
                        type += $" (${j.Value.Value:X2})";
                    else
                        type += $" (${j.Value.Value:X4})";

                    _variables.Add(new VariableMap(i.Value.Name, type, getter));
                }
            }

            if (s.Parent != null)
                s = s.Parent;
            else
                break;
        }
    }
}
