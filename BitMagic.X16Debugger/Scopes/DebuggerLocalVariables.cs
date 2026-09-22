using BitMagic.Common;
using BitMagic.X16Debugger.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;
using System;

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

    public void SetLocalScope(StackFrameState? state, Emulator emulator, ExpressionManager expressionManager, VariableManager variableManager)
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

                var toAdd = GetVariable(i.Key, i.Value, expressionManager, memory, emulator, variableManager);

                _variables.Add(toAdd);
                //if (i.Value.VariableType == VariableType.DebuggerExpression)
                //{
                //    var debugVar = j.Value as DebuggerVariable ?? throw new Exception("Variable claims to be a debugger expression but isnt.");

                //    Func<string> getter = debugVar.ToExpressionFunction(expressionManager);

                //    var type = j.Value.VariableTypeText();

                //    _variables.Add(new VariableMap(i.Value.Name, type, getter));
                //}
                //else
                //{
                //    // todo: handle arrays
                //    //if (j.Value.Array)
                //    //{
                //    //    var array = 
                //    //    var variableIndex = new VariableIndex(i.Value.Name, () => { 

                //    //    })

                //    //    continue;
                //    //}

                //    Func<string> getter = j.Value.ToStringFunction(memory);

                //    var type = j.Value.VariableTypeText();

                //    if (j.Value.Value < 256)
                //        type += $" (${j.Value.Value:X2})";
                //    else
                //        type += $" (${j.Value.Value:X4})";

                //    _variables.Add(new VariableMap(i.Value.Name, type, getter));
                //}
            }

            if (s.Parent != null)
                s = s.Parent;
            else
                break;
        }
    }

    // Also reused by VariableManager.RebuildGlobals - this rendering doesn't depend on
    // instance state, just the variable/memory/manager it's given.
    internal static IVariableItem? GetVariable(string name, IAsmVariable variable, ExpressionManager expressionManager, MemoryWrapper memory, Emulator emulator, VariableManager variableManager)
    {
        var j = variable;

        if (variable.VariableType == VariableType.DebuggerExpression)
        {
            var debugVar = j as DebuggerVariable ?? throw new Exception("Variable claims to be a debugger expression but isnt.");

            Func<string> getter = debugVar.ToExpressionFunction(expressionManager);

            var type = j.VariableTypeText();

            // Arbitrary expressions aren't a single memory location, so there's no sensible write-back.
            return new VariableMap(variable.Name, type, getter);
        }
        else
        {
            // todo: handle arrays
            if (j.Array)
            {
                var v = new VariableIndex(name, GetArray(j, memory), GetArraySetter(j, emulator));
                variableManager.Register(v);
                return v;
            }

            Func<string> getter = j.ToStringFunction(memory);

            var type = j.VariableTypeText();

            if (j.Value < 256)
                type += $" (${j.Value:X2})";
            else
                type += $" (${j.Value:X4})";

            Action<string>? setValue = j.SupportsDirectWrite() ? value => j.TrySetValue(emulator, value) : null;

            return new VariableMap(name, type, getter, setValue: setValue);
        }

        return null;
    }

    internal static Func<(string Value, ICollection<Variable> Variables)> GetArray(IAsmVariable variable, MemoryWrapper memory)
    {
        var _variable = variable;
        var _memory = memory;

        return () => {
            var toReturn = new List<Variable>();

            for (var i = 0; i < _variable.Length; i++)
            {
                Func<string> getter = _variable.ToStringFunction(_memory, i);

                var type = _variable.VariableTypeText();
                var value = _variable.MemoryOffset(i);

                if (value < 256)
                    type += $" (${value:X2})";
                else
                    type += $" (${value:X4})";

                var x = new VariableMap(i.ToString(), type, getter);

                toReturn.Add(x.GetVariable());
            }

            return ($"{_variable.VariableTypeText()}[{_variable.Length.ToString()}]", toReturn);
        };
    }

    // Element name is the index (as text, set by GetArray above) - out-of-range or non-numeric
    // names are ignored rather than throwing, since VSC is the only caller and always echoes
    // back a name it was given.
    internal static Action<string, string>? GetArraySetter(IAsmVariable variable, Emulator emulator)
    {
        if (!variable.SupportsDirectWrite())
            return null;

        return (indexText, value) =>
        {
            if (int.TryParse(indexText, out var index) && index >= 0 && index < variable.Length)
                variable.TrySetValue(emulator, value, index);
        };
    }
}
