using BitMagic.Common;
using BitMagic.Compiler;
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

    public void SetLocalScope(StackFrameState? state, Emulator emulator)
    {
        _variables.Clear();
        if (state == null || state.Scope == null)
            return;

        var memory = new MemoryWrapper(() => emulator.Memory.ToArray());

        foreach (var i in state.Scope.Variables.Values)
        {
            if (i.Value.VariableType is VariableType.Constant or VariableType.ProcStart or VariableType.ProcEnd or VariableType.SegmentStart or VariableType.LabelPointer)
                continue;

            var j = i;
            Func<string> getter = j.Value.ToStringFunction(memory);


            //    j.Value.VariableType switch
            //{
            //    VariableType.Byte => () => $"0x{memory[j.Value.Value].Byte:X2}",
            //    VariableType.Sbyte => () => $"{memory[j.Value.Value].Sbyte:0}",
            //    VariableType.Short => () => $"{memory[j.Value.Value].Short:0}",
            //    VariableType.Ushort => () => $"0x{memory[j.Value.Value].Ushort:X4}",
            //    VariableType.Int => () => $"{memory[j.Value.Value].Int:0}",
            //    VariableType.Uint => () => $"0x{memory[j.Value.Value].Uint:X8}",
            //    VariableType.Long => () => $"{memory[j.Value.Value].Long:0}",
            //    VariableType.Ulong => () => $"0x{memory[j.Value.Value].Ulong:X16}",
            //    VariableType.String => () => memory[j.Value.Value].String,
            //    VariableType.FixedStrings => () => memory[j.Value.Value].FixedString(j.Value.Length),
            //    _ => () => "Unhandled"
            //};

            var type = j.Value.VariableTypeText();
            //var type = j.Value.VariableType switch
            //{
            //    VariableType.Byte => "int",
            //    VariableType.Sbyte => "int",
            //    VariableType.Short => "int",
            //    VariableType.Ushort => "int",
            //    VariableType.Int => "int",
            //    VariableType.Uint => "int",
            //    VariableType.Long => "int",
            //    VariableType.Ulong => "int",
            //    VariableType.String => "string",
            //    VariableType.FixedStrings => "string",
            //    _ => "string"
            //};

            _variables.Add(new VariableMap(i.Value.Name, type, getter));
        }
    }
}

internal static class IAsmVariableExtensions
{
    internal static Func<string> ToStringFunction(this IAsmVariable variable, MemoryWrapper memory) =>
        variable.VariableType switch
        {
            VariableType.Constant => () => $"{variable.Value}",
            VariableType.ProcStart => () => $"0x{variable.Value:X4}",
            VariableType.ProcEnd => () => $"0x{variable.Value:X4}",
            VariableType.SegmentStart => () => $"0x{variable.Value:X4}",
            VariableType.LabelPointer => () => $"0x{variable.Value:X4}",
            VariableType.Byte => () => $"0x{memory[variable.Value].Byte:X2}",
            VariableType.Sbyte => () => $"{memory[variable.Value].Sbyte:0}",
            VariableType.Short => () => $"{memory[variable.Value].Short:0}",
            VariableType.Ushort => () => $"0x{memory[variable.Value].Ushort:X4}",
            VariableType.Int => () => $"{memory[variable.Value].Int:0}",
            VariableType.Uint => () => $"0x{memory[variable.Value].Uint:X8}",
            VariableType.Long => () => $"{memory[variable.Value].Long:0}",
            VariableType.Ulong => () => $"0x{memory[variable.Value].Ulong:X16}",
            VariableType.String => () => memory[variable.Value].String,
            VariableType.FixedStrings => () => memory[variable.Value].FixedString(variable.Length),
            _ => () => "Unhandled"
        };

    internal static object GetActualValue(this IAsmVariable variable, MemoryWrapper memory) =>
        variable.VariableType switch
        {
            VariableType.Constant => (int)variable.Value,
            VariableType.ProcStart => (ushort)variable.Value,
            VariableType.ProcEnd =>  (ushort)variable.Value,
            VariableType.SegmentStart => (ushort)variable.Value,
            VariableType.LabelPointer => (ushort)variable.Value,
            VariableType.Byte => memory[variable.Value].Byte,
            VariableType.Sbyte => memory[variable.Value].Sbyte,
            VariableType.Short => memory[variable.Value].Short,
            VariableType.Ushort => memory[variable.Value].Ushort,
            VariableType.Int => memory[variable.Value].Int,
            VariableType.Uint => memory[variable.Value].Uint,
            VariableType.Long => memory[variable.Value].Long,
            VariableType.Ulong => memory[variable.Value].Ulong,
            VariableType.String => memory[variable.Value].String,
            VariableType.FixedStrings => memory[variable.Value].FixedString(variable.Length),
            _ => "Unhandled"
        };

    internal static string VariableTypeText(this IAsmVariable variable) =>
         variable.VariableType switch
         {
             VariableType.Byte => "int",
             VariableType.Sbyte => "int",
             VariableType.Short => "int",
             VariableType.Ushort => "int",
             VariableType.Int => "int",
             VariableType.Uint => "int",
             VariableType.Long => "int",
             VariableType.Ulong => "int",
             VariableType.String => "string",
             VariableType.FixedStrings => "string",
             _ => "string"
         };
}