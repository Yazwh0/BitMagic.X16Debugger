using BitMagic.Common;
using BitMagic.Compiler;
using BitMagic.X16Debugger.Variables;
using BitMagic.X16Emulator;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Scope = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Scope;

namespace BitMagic.X16Debugger;

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

internal interface IScopeMap
{
    IEnumerable<IVariableItem> Variables { get; }
    int Id { get; }
    Scope Scope { get; }
}

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

internal static class IAsmVariableExtensions
{
    internal static Func<string> ToExpressionFunction(this DebuggerVariable variable, ExpressionManager expressionManager)
        => () => expressionManager.Evaluate(variable.Expression);

    internal static Func<string> ToStringFunction(this IAsmVariable variable, MemoryWrapper memory) =>
        variable.VariableDataType switch
        {
            VariableDataType.Constant => () => $"{variable.Value}",
            VariableDataType.ProcStart => () => $"0x{variable.Value:X4}",
            VariableDataType.ProcEnd => () => $"0x{variable.Value:X4}",
            VariableDataType.SegmentStart => () => $"0x{variable.Value:X4}",
            VariableDataType.LabelPointer => () => $"0x{variable.Value:X4}",
            VariableDataType.Byte => () => $"0x{memory[variable.Value].Byte:X2}",
            VariableDataType.Sbyte => () => $"{memory[variable.Value].Sbyte:0}",
            VariableDataType.Short => () => $"{memory[variable.Value].Short:0}",
            VariableDataType.Ushort => () => $"0x{memory[variable.Value].Ushort:X4}",
            VariableDataType.Int => () => $"{memory[variable.Value].Int:0}",
            VariableDataType.Uint => () => $"0x{memory[variable.Value].Uint:X8}",
            VariableDataType.Long => () => $"{memory[variable.Value].Long:0}",
            VariableDataType.Ulong => () => $"0x{memory[variable.Value].Ulong:X16}",
            VariableDataType.String => () => memory[variable.Value].String,
            VariableDataType.FixedStrings => () => memory[variable.Value].FixedString(variable.Length),
            _ => () => "Unhandled"
        };

    private static Func<string> ToStringValue(int value, int length, VariableDataType dataType, MemoryWrapper memory) =>
       dataType switch
        {
            VariableDataType.Constant => () => $"{value}",
            VariableDataType.ProcStart => () => $"0x{value:X4}",
            VariableDataType.ProcEnd => () => $"0x{value:X4}",
            VariableDataType.SegmentStart => () => $"0x{value:X4}",
            VariableDataType.LabelPointer => () => $"0x{value:X4}",
            VariableDataType.Byte => () => $"0x{memory[value].Byte:X2}",
            VariableDataType.Sbyte => () => $"{memory[value].Sbyte:0}",
            VariableDataType.Short => () => $"{memory[value].Short:0}",
            VariableDataType.Ushort => () => $"0x{memory[value].Ushort:X4}",
            VariableDataType.Int => () => $"{memory[value].Int:0}",
            VariableDataType.Uint => () => $"0x{memory[value].Uint:X8}",
            VariableDataType.Long => () => $"{memory[value].Long:0}",
            VariableDataType.Ulong => () => $"0x{memory[value].Ulong:X16}",
            VariableDataType.String => () => memory[value].String,
            VariableDataType.FixedStrings => () => memory[value].FixedString(length),
            _ => () => "Unhandled"
        };

    internal static object GetActualValue(this IAsmVariable variable, MemoryWrapper memory) =>
        variable.VariableDataType switch
        {
            VariableDataType.Constant => (int)variable.Value,
            VariableDataType.ProcStart => (ushort)variable.Value,
            VariableDataType.ProcEnd => (ushort)variable.Value,
            VariableDataType.SegmentStart => (ushort)variable.Value,
            VariableDataType.LabelPointer => (ushort)variable.Value,
            VariableDataType.Byte => memory[variable.Value].Byte,
            VariableDataType.Sbyte => memory[variable.Value].Sbyte,
            VariableDataType.Short => memory[variable.Value].Short,
            VariableDataType.Ushort => memory[variable.Value].Ushort,
            VariableDataType.Int => memory[variable.Value].Int,
            VariableDataType.Uint => memory[variable.Value].Uint,
            VariableDataType.Long => memory[variable.Value].Long,
            VariableDataType.Ulong => memory[variable.Value].Ulong,
            VariableDataType.String => memory[variable.Value].String,
            VariableDataType.FixedStrings => memory[variable.Value].FixedString(variable.Length),
            _ => "Unhandled"
        };

    internal static string VariableTypeText(this IAsmVariable variable) =>
         variable.VariableDataType switch
         {
             VariableDataType.Byte => "byte",
             VariableDataType.Sbyte => "sbyte",
             VariableDataType.Short => "short",
             VariableDataType.Ushort => "ushort",
             VariableDataType.Int => "int",
             VariableDataType.Uint => "uint",
             VariableDataType.Long => "long",
             VariableDataType.Ulong => "ulong",
             VariableDataType.String => "string",
             VariableDataType.FixedStrings => "string",
             _ => "string"
         };
}