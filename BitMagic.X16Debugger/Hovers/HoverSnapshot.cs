using BitMagic.Common;
using BitMagic.Compiler;

namespace BitMagic.X16Debugger.Hovers;

/// <summary>
/// One BitMagic file from a build: its compile state, and how its source lines map to the lines that were compiled.
/// </summary>
public sealed class HoverUnit
{
    public string SourcePath { get; }
    public CompileState State { get; }

    /// <summary>The template engine's output, which is what was compiled. Null if the source was compiled as is.</summary>
    public ISourceFile? Generated { get; }

    // compiled output by line of the compiled code (1 based)
    private readonly Dictionary<int, IOutputData> _output = new();

    // (source file, 0 based line) -> 1 based line of the compiled code
    private readonly Dictionary<(string Path, int Line), int> _sourceToCompiled = new();

    public HoverUnit(string sourcePath, CompileState state, ISourceFile? generated)
    {
        SourcePath = sourcePath.FixFilename();
        State = state;
        Generated = generated;

        foreach (var segment in state.Segments.Values)
        {
            foreach (var procedure in segment.DefaultProcedure.Values)
                AddOutput(procedure);
        }

        if (generated == null)
            return;

        var parentPaths = generated.Parents.Select(i => i.Path.FixFilename()).ToList();

        for (var i = 0; i < generated.ParentMap.Count; i++)
        {
            var map = generated.ParentMap[i];
            if (map.relativeId < 0 || map.relativeId >= parentPaths.Count || map.relativeLineNumber < 0)
                continue;

            // a line that generates several (a template loop) maps to the first
            _sourceToCompiled.TryAdd((parentPaths[map.relativeId], map.relativeLineNumber), i + 1);
        }
    }

    private void AddOutput(Procedure procedure)
    {
        foreach (var data in procedure.Data)
            _output.TryAdd(data.Source.LineNumber, data);

        foreach (var child in procedure.Procedures)
            AddOutput(child);
    }

    /// <summary>The line of the compiled code (1 based) a source line (0 based) became, if it's in this unit.</summary>
    public int? CompiledLine(string path, int line)
    {
        if (Generated == null)
            return path == SourcePath ? line + 1 : null;

        return _sourceToCompiled.TryGetValue((path, line), out var compiled) ? compiled : null;
    }

    public string? CompiledText(int compiledLine)
    {
        var content = Generated?.Content;
        return content != null && compiledLine >= 1 && compiledLine <= content.Count ? content[compiledLine - 1] : null;
    }

    public IOutputData? Output(int compiledLine) => _output.TryGetValue(compiledLine, out var data) ? data : null;
}

/// <summary>
/// The last build that succeeded, for the hover. Swapped as a whole after each good build, so a failed or
/// half finished build never shows.
/// </summary>
public sealed class HoverSnapshot(IReadOnlyList<HoverUnit> units)
{
    public IReadOnlyList<HoverUnit> Units { get; } = units;

    public (HoverUnit Unit, int CompiledLine)? Find(string path, int line)
    {
        path = path.FixFilename();

        foreach (var unit in Units)
        {
            if (unit.CompiledLine(path, line) is int compiled)
                return (unit, compiled);
        }

        return null;
    }

    /// <summary>
    /// The hover for a position in a source file, from this build. Null if there's nothing to say.
    /// </summary>
    public (HoverResult Result, int Start, int End)? Hover(string path, int line, string lineText, int column, IHoverRuntime? runtime = null)
    {
        var target = HoverText.Find(lineText, column);
        if (target == null)
            return null;

        var found = Find(path, line);
        if (found == null)
            return null;

        var (unit, compiledLine) = found.Value;
        var procedure = unit.State.ProcedureAt(compiledLine);
        if (procedure == null)
            return null;

        var output = unit.Output(compiledLine);
        var compiledText = unit.CompiledText(compiledLine);
        string? generatedLine = null;

        // template code: what's shown isn't what was compiled, so work from the compiled line
        if (compiledText != null && Normalise(compiledText) != Normalise(lineText))
        {
            generatedLine = compiledText;

            var compiled = compiledText.TrimEnd();
            var compiledTarget = HoverText.Find(compiled, compiled.Length);

            target = new HoverTarget
            {
                Kind = compiledTarget?.Kind ?? target.Kind,
                Keyword = compiledTarget?.Keyword ?? target.Keyword,
                Name = target.Name,
                NameStart = target.NameStart,
                NameEnd = target.NameEnd,
                DefinedName = compiledTarget?.DefinedName,
                Operand = compiledTarget?.Operand,
                Expression = compiledTarget?.Expression,
                Prefix = compiledTarget?.Prefix ?? "",
                Suffix = compiledTarget?.Suffix ?? ""
            };
        }

        HoverResult? text;
        lock (unit)
        {
            text = HoverBuilder.Build(target, new HoverBuilder.Context
            {
                State = unit.State,
                Procedure = procedure,
                Output = output,
                GeneratedLine = generatedLine,
                Runtime = runtime
            });
        }

        if (text == null)
            return null;

        // template code: the operand's columns are in the compiled line, so highlight the name or the whole line
        if (generatedLine != null)
        {
            if (target.Name != null)
                return (text, target.NameStart, target.NameEnd);

            var start = lineText.Length - lineText.TrimStart().Length;
            return (text, start, lineText.TrimEnd().Length);
        }

        return target.Name != null && target.Expression == target.Name || target.Operand == null ?
            (text, target.NameStart, target.NameEnd) :
            (text, target.OperandStart, target.OperandEnd);
    }

    private static string Normalise(string line)
    {
        var idx = line.IndexOf(';');
        return (idx == -1 ? line : line[..idx]).Trim();
    }
}
