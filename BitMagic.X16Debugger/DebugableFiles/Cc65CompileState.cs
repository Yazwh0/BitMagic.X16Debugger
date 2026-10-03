using BitMagic.Cc65Lib;
using BitMagic.Common;
using BitMagic.Compiler;
using CompilerVariables = BitMagic.Compiler.Variables;

namespace BitMagic.X16Debugger.DebugableFiles;

/// <summary>
/// Creates a CompileState from the symbols in a ld65 debug file, so cc65 symbols are used by the debugger in the
/// same way as BitMagic ones. Module level symbols go in the default procedure (as top level BitMagic labels do),
/// and each named cc65 scope becomes a Procedure.
/// </summary>
internal static class Cc65CompileState
{
    public static (CompileState State, Dictionary<DebugScope, Procedure> Scopes) Build(DebugInfo debugInfo, string name)
    {
        var state = new CompileState(new CompilerVariables("App"), name);
        var root = state.Procedure;
        var scopes = new Dictionary<DebugScope, Procedure>();

        foreach (var scope in debugInfo.Scopes.Where(i => i.Parent == null))
            AddScope(scope, root, scopes);

        // labels on data (eg tables) are variables, labels on instructions are not.
        var layout = new SymbolLayout(debugInfo);

        // module level symbols from different modules share a procedure, so a name can be repeated. MakeExplicit
        // throws if a label clashes with another variable, so the first one wins.
        var added = new HashSet<(Procedure, string)>();

        var symbols = debugInfo.Symbols
            .Where(i => i.Type != DebugSymbolType.Import)
            .Select(i => (Symbol: i, Procedure: i.Scope != null && scopes.TryGetValue(i.Scope, out var p) ? p : root, Type: GetVariableType(i, layout)))
            .OrderBy(i => i.Type.Type == VariableDataType.LabelPointer); // variables first, so they win over labels

        foreach (var (symbol, procedure, (type, length, array)) in symbols)
        {
            // repeated labels (eg cheap locals) are fine, they are left ambiguous.
            if (type == VariableDataType.LabelPointer ? added.Contains((procedure, symbol.Name)) : !added.Add((procedure, symbol.Name)))
                continue;

            procedure.Variables.SetValue(symbol.Name, symbol.Value, type, false, length, array);

            // labels are held as ambiguous until MakeExplicit, they aren't shown so don't need a segment.
            if (procedure.Variables.Values.TryGetValue(symbol.Name.Trim(), out var variable) && variable is AsmVariable asmVariable)
                asmVariable.Segment = symbol.Segment?.Name;
        }

        // labels are added as ambiguous, this makes the unique ones resolvable by name.
        state.ScopeFactory.GlobalVariables.MakeExplicit();

        return (state, scopes);
    }

    private static void AddScope(DebugScope scope, Procedure parent, Dictionary<DebugScope, Procedure> scopes)
    {
        // a module's top level scope is unnamed, its symbols are global.
        var procedure = scope.Parent == null || string.IsNullOrEmpty(scope.Name) ? parent : new Procedure(scope.Name, parent);
        scopes[scope] = procedure;

        foreach (var child in scope.Children)
            AddScope(child, procedure, scopes);
    }

    internal static (VariableDataType Type, int Length, bool Array) GetVariableType(DebugSymbol symbol, SymbolLayout layout)
    {
        var segment = symbol.Segment;

        if (segment == null)
            return NotVariable(symbol);

        int? size = null;

        if (symbol.Type == DebugSymbolType.Equate)
        {
            // eg zero page variables assigned by hand, 'row_count = $ca'
            if (segment.IsWritable)
                size = symbol.Size ?? 1;
        }
        else if (segment.IsWritable || layout.IsData(segment, symbol.Value))
        {
            size = symbol.Size ?? layout.SizeToNextLabel(segment, symbol.Value);
        }

        if (size == null)
            return NotVariable(symbol);

        // ca65 symbols have no type, so go by size.
        return size switch
        {
            <= 1 => (VariableDataType.Byte, 1, false),
            2 => (VariableDataType.Ushort, 1, false),
            _ => (VariableDataType.Byte, size.Value, true)
        };
    }

    private static (VariableDataType Type, int Length, bool Array) NotVariable(DebugSymbol symbol) =>
        (symbol.Type == DebugSymbolType.Label ? VariableDataType.LabelPointer : VariableDataType.Constant, 0, false);

    /// <summary>
    /// Where data and labels are within each segment, to tell data labels from code labels and size them.
    /// </summary>
    internal sealed class SymbolLayout
    {
        // ld65 only gives data spans (eg .byte, .word) a type, instructions have none.
        private readonly HashSet<(int SegmentId, int Offset)> _dataStarts;
        private readonly Dictionary<int, int[]> _labelAddresses;

        public SymbolLayout(DebugInfo debugInfo)
        {
            _dataStarts = debugInfo.Spans.Where(i => i.TypeId != null).Select(i => (i.SegmentId, i.Start)).ToHashSet();
            _labelAddresses = debugInfo.Symbols
                .Where(i => i.Type == DebugSymbolType.Label && i.Segment != null)
                .GroupBy(i => i.Segment!.SegmentId)
                .ToDictionary(i => i.Key, i => i.Select(j => j.Value).Distinct().Order().ToArray());
        }

        public bool IsData(DebugSegment segment, int address) => _dataStarts.Contains((segment.SegmentId, address - segment.Start));

        /// <summary>
        /// Labels without a size (eg a table of .word) run to the next label, or the end of the segment.
        /// </summary>
        public int SizeToNextLabel(DebugSegment segment, int address)
        {
            var end = segment.Start + segment.Size;

            if (_labelAddresses.TryGetValue(segment.SegmentId, out var addresses))
            {
                var index = Array.BinarySearch(addresses, address);
                index = index < 0 ? ~index : index + 1;

                if (index < addresses.Length)
                    end = Math.Min(end, addresses[index]);
            }

            return Math.Max(1, end - address);
        }
    }
}
