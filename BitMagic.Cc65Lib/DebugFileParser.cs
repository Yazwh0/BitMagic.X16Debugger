using System.Globalization;
using System.Runtime.CompilerServices;

namespace BitMagic.Cc65Lib;

/// <summary>
/// Parses the debug info file written by ld65 (--dbgfile). Line -> span -> segment records give the
/// final linked location of every source line, so no config or object file reconstruction is needed.
/// Spans are only present if the source was assembled with -g.
/// </summary>
public static class DebugFileParser
{
    public static DebugInfo ParseFile(string filename) => Parse(File.ReadAllLines(filename));

    public static DebugInfo Parse(IEnumerable<string> contents)
    {
        var toReturn = new DebugInfo();

        foreach (var line in contents)
        {
            var idx = line.IndexOf('\t');
            if (idx == -1) continue;

            var span = line.AsSpan();
            var key = span[..idx];
            var pairs = span[(idx + 1)..];

            if (SpanEquals(key, "line"))
                toReturn.Lines.Add(DebugLine.FromPairs(GetPairs(pairs)));
            else if (SpanEquals(key, "span"))
                toReturn.Spans.Add(DebugSpan.FromPairs(GetPairs(pairs)));
            else if (SpanEquals(key, "file"))
                toReturn.SourceFiles.Add(DebugSourceFile.FromPairs(GetPairs(pairs)));
            else if (SpanEquals(key, "mod"))
                toReturn.Modules.Add(DebugModule.FromPairs(GetPairs(pairs)));
            else if (SpanEquals(key, "seg"))
                toReturn.Segments.Add(DebugSegment.FromPairs(GetPairs(pairs)));
            else if (SpanEquals(key, "scope"))
                toReturn.Scopes.Add(DebugScope.FromPairs(GetPairs(pairs)));
            else if (SpanEquals(key, "sym"))
                toReturn.Symbols.Add(DebugSymbol.FromPairs(GetPairs(pairs)));
            else if (SpanEquals(key, "lib"))
                toReturn.Libraries.Add(DebugLibrary.FromPairs(GetPairs(pairs)));
        }

        toReturn.Link();

        return toReturn;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SpanEquals(ReadOnlySpan<char> span, string text)
    {
        return span.Equals(text.AsSpan(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Splits 'key=value,key="value",...'. Commas and equals inside quotes are part of the value, quotes are removed.
    /// </summary>
    public static Dictionary<string, string> GetPairs(ReadOnlySpan<char> span)
    {
        var pairs = new Dictionary<string, string>();

        var keyStart = 0;
        var equalsIndex = -1;
        var inQuotes = false;

        for (var i = 0; i <= span.Length; i++)
        {
            if (i < span.Length)
            {
                var c = span[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (inQuotes)
                    continue;

                if (c == '=' && equalsIndex == -1)
                {
                    equalsIndex = i;
                    continue;
                }

                if (c != ',')
                    continue;
            }

            if (equalsIndex != -1)
            {
                var value = span.Slice(equalsIndex + 1, i - equalsIndex - 1).ToString();
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    value = value[1..^1];

                pairs[span.Slice(keyStart, equalsIndex - keyStart).ToString()] = value;
            }

            keyStart = i + 1;
            equalsIndex = -1;
        }

        return pairs;
    }

    /// <summary>
    /// Parses a number in either '0x' hex or decimal.
    /// </summary>
    internal static int ParseNumber(string value) =>
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : int.Parse(value, CultureInfo.InvariantCulture);

    internal static long ParseLong(string value) =>
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : long.Parse(value, CultureInfo.InvariantCulture);

    internal static List<int> ParseIdList(string value) =>
        value.Split('+', StringSplitOptions.RemoveEmptyEntries).Select(ParseNumber).ToList();
}

public class DebugInfo
{
    public List<DebugSourceFile> SourceFiles { get; set; } = new();
    public List<DebugLine> Lines { get; set; } = new();
    public List<DebugSpan> Spans { get; set; } = new();
    public List<DebugModule> Modules { get; set; } = new();
    public List<DebugSegment> Segments { get; set; } = new();
    public List<DebugScope> Scopes { get; set; } = new();
    public List<DebugLibrary> Libraries { get; set; } = new();
    public List<DebugSymbol> Symbols { get; set; } = new();

    /// <summary>
    /// Segments that are written to an output file, grouped by the output filename as ld65 wrote it.
    /// </summary>
    public IEnumerable<IGrouping<string, DebugSegment>> OutputFiles =>
        Segments.Where(i => !string.IsNullOrEmpty(i.OutputFile)).GroupBy(i => i.OutputFile);

    internal void Link()
    {
        var files = SourceFiles.ToDictionary(i => i.FileId);
        var modules = Modules.ToDictionary(i => i.ModuleId);
        var segments = Segments.ToDictionary(i => i.SegmentId);
        var spans = Spans.ToDictionary(i => i.SpanId);
        var libraries = Libraries.ToDictionary(i => i.LibraryId);

        foreach (var span in Spans)
        {
            span.Segment = segments.GetValueOrDefault(span.SegmentId);
        }

        foreach (var file in SourceFiles)
        {
            if (modules.TryGetValue(file.ModuleId, out var module))
                module.SourceFiles.Add(file);
        }

        foreach (var line in Lines)
        {
            line.SourceFile = files.GetValueOrDefault(line.FileId);
            line.SourceFile?.Lines.Add(line);

            foreach (var spanId in line.SpanIds)
            {
                if (spans.TryGetValue(spanId, out var span))
                    line.Spans.Add(span);
            }
        }

        foreach (var module in Modules)
        {
            module.MainSourceFile = files.GetValueOrDefault(module.FileId);
            module.LibrarySourceFile = libraries.GetValueOrDefault(module.LibraryId);
        }

        foreach (var scope in Scopes)
        {
            foreach (var spanId in scope.SpanIds)
            {
                if (spans.TryGetValue(spanId, out var span))
                    scope.Spans.Add(span);
            }

            // the unnamed top level scope of a module covers everything that module contributed to each segment
            if (scope.ParentId == -1 && modules.TryGetValue(scope.ModuleId, out var module))
                module.RootScope = scope;
        }

        var scopes = Scopes.ToDictionary(i => i.ScopeId);

        foreach (var scope in Scopes)
        {
            scope.Parent = scopes.GetValueOrDefault(scope.ParentId);
            scope.Parent?.Children.Add(scope);
        }

        var symbols = Symbols.ToDictionary(i => i.SymbolId);

        foreach (var symbol in Symbols)
        {
            symbol.Segment = symbol.SegmentId == -1 ? null : segments.GetValueOrDefault(symbol.SegmentId);
            symbol.Scope = scopes.GetValueOrDefault(symbol.ScopeId);
        }

        // cheap locals (@name) have a parent symbol rather than a scope
        foreach (var symbol in Symbols.Where(i => i.Scope == null && i.ParentId != -1))
        {
            symbol.Scope = symbols.GetValueOrDefault(symbol.ParentId)?.Scope;
        }
    }
}

public class DebugSourceFile
{
    public int FileId { get; set; }
    public string Name { get; set; } = "";
    public int Size { get; set; }
    /// <summary>
    /// Unix time of the source file when it was assembled.
    /// </summary>
    public long ModifiedTime { get; set; }
    public int ModuleId { get; set; }

    public List<DebugLine> Lines { get; set; } = [];

    public static DebugSourceFile FromPairs(Dictionary<string, string> pairs)
    {
        var toReturn = new DebugSourceFile();
        if (pairs.TryGetValue("id", out var fileId))
            toReturn.FileId = DebugFileParser.ParseNumber(fileId);
        if (pairs.TryGetValue("name", out var name))
            toReturn.Name = name;
        if (pairs.TryGetValue("size", out var size))
            toReturn.Size = DebugFileParser.ParseNumber(size);
        if (pairs.TryGetValue("mtime", out var mtime))
            toReturn.ModifiedTime = DebugFileParser.ParseLong(mtime);
        if (pairs.TryGetValue("mod", out var moduleId))
            // a file can be used by several modules, 'mod=1+2'. Only the first is linked.
            toReturn.ModuleId = DebugFileParser.ParseIdList(moduleId).FirstOrDefault();
        return toReturn;
    }
}

public class DebugLibrary
{
    public int LibraryId { get; set; }
    public string Name { get; set; } = "";

    public static DebugLibrary FromPairs(Dictionary<string, string> pairs)
    {
        var toReturn = new DebugLibrary();
        if (pairs.TryGetValue("id", out var fileId))
            toReturn.LibraryId = DebugFileParser.ParseNumber(fileId);
        if (pairs.TryGetValue("name", out var name))
            toReturn.Name = name;
        return toReturn;
    }
}

public enum DebugLineType
{
    Assembler = 0,
    External = 1, // eg C source when the assembler source was generated by cc65
    Macro = 2
}

public class DebugLine
{
    public int LineId { get; set; }
    public int FileId { get; set; }
    /// <summary>
    /// 1 based line number.
    /// </summary>
    public int LineNumber { get; set; }
    public DebugLineType Type { get; set; } = DebugLineType.Assembler;
    public int Count { get; set; }
    public List<int> SpanIds { get; set; } = [];

    public DebugSourceFile? SourceFile { get; set; }
    public List<DebugSpan> Spans { get; set; } = [];

    public static DebugLine FromPairs(Dictionary<string, string> pairs)
    {
        var toReturn = new DebugLine();
        if (pairs.TryGetValue("id", out var lineId))
            toReturn.LineId = DebugFileParser.ParseNumber(lineId);
        if (pairs.TryGetValue("file", out var fileId))
            toReturn.FileId = DebugFileParser.ParseNumber(fileId);
        if (pairs.TryGetValue("line", out var lineNumber))
            toReturn.LineNumber = DebugFileParser.ParseNumber(lineNumber);
        if (pairs.TryGetValue("type", out var typeId))
            toReturn.Type = (DebugLineType)DebugFileParser.ParseNumber(typeId);
        if (pairs.TryGetValue("count", out var count))
            toReturn.Count = DebugFileParser.ParseNumber(count);
        if (pairs.TryGetValue("span", out var spans))
            toReturn.SpanIds = DebugFileParser.ParseIdList(spans);
        return toReturn;
    }
}

/// <summary>
/// A range of bytes within a linked segment.
/// </summary>
public class DebugSpan
{
    public int SpanId { get; set; }
    public int SegmentId { get; set; }
    /// <summary>
    /// Offset from the start of the segment.
    /// </summary>
    public int Start { get; set; }
    public int Size { get; set; }
    /// <summary>
    /// Id of the data type, only set for data (eg .byte, .word), not instructions.
    /// </summary>
    public int? TypeId { get; set; }

    public DebugSegment? Segment { get; set; }

    public static DebugSpan FromPairs(Dictionary<string, string> pairs)
    {
        var toReturn = new DebugSpan();
        if (pairs.TryGetValue("id", out var spanId))
            toReturn.SpanId = DebugFileParser.ParseNumber(spanId);
        if (pairs.TryGetValue("seg", out var segmentId))
            toReturn.SegmentId = DebugFileParser.ParseNumber(segmentId);
        if (pairs.TryGetValue("start", out var start))
            toReturn.Start = DebugFileParser.ParseNumber(start);
        if (pairs.TryGetValue("size", out var size))
            toReturn.Size = DebugFileParser.ParseNumber(size);
        if (pairs.TryGetValue("type", out var typeId))
            toReturn.TypeId = DebugFileParser.ParseNumber(typeId);
        return toReturn;
    }
}

public class DebugModule
{
    public int ModuleId { get; set; }
    public string Name { get; set; } = "";
    public int FileId { get; set; } = -1;
    public int LibraryId { get; set; } = -1;

    public List<DebugSourceFile> SourceFiles { get; set; } = [];
    public DebugSourceFile? MainSourceFile { get; set; } = null;
    public DebugLibrary? LibrarySourceFile { get; set; } = null;
    public DebugScope? RootScope { get; set; } = null;

    public static DebugModule FromPairs(Dictionary<string, string> pairs)
    {
        var toReturn = new DebugModule();
        if (pairs.TryGetValue("id", out var moduleId))
            toReturn.ModuleId = DebugFileParser.ParseNumber(moduleId);
        if (pairs.TryGetValue("name", out var name))
            toReturn.Name = name;
        if (pairs.TryGetValue("file", out var fileId))
            toReturn.FileId = DebugFileParser.ParseNumber(fileId);
        if (pairs.TryGetValue("lib", out var libraryId))
            toReturn.LibraryId = DebugFileParser.ParseNumber(libraryId);
        return toReturn;
    }
}

public class DebugScope
{
    public int ScopeId { get; set; }
    public string Name { get; set; } = "";
    public int ModuleId { get; set; } = -1;
    public int ParentId { get; set; } = -1;
    public List<int> SpanIds { get; set; } = [];

    public List<DebugSpan> Spans { get; set; } = [];
    public DebugScope? Parent { get; set; }
    public List<DebugScope> Children { get; set; } = [];

    public static DebugScope FromPairs(Dictionary<string, string> pairs)
    {
        var toReturn = new DebugScope();
        if (pairs.TryGetValue("id", out var scopeId))
            toReturn.ScopeId = DebugFileParser.ParseNumber(scopeId);
        if (pairs.TryGetValue("name", out var name))
            toReturn.Name = name;
        if (pairs.TryGetValue("mod", out var moduleId))
            toReturn.ModuleId = DebugFileParser.ParseNumber(moduleId);
        if (pairs.TryGetValue("parent", out var parentId))
            toReturn.ParentId = DebugFileParser.ParseNumber(parentId);
        if (pairs.TryGetValue("span", out var spans))
            toReturn.SpanIds = DebugFileParser.ParseIdList(spans);
        return toReturn;
    }
}

public class DebugSegment
{
    public int SegmentId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>
    /// Run address of the segment.
    /// </summary>
    public int Start { get; set; }
    public int Size { get; set; }
    /// <summary>
    /// Output file the segment is written to, empty if it is not written (eg BSS, ZEROPAGE).
    /// </summary>
    public string OutputFile { get; set; } = "";
    /// <summary>
    /// Offset of the segment within the output file.
    /// </summary>
    public int Offset { get; set; }
    /// <summary>
    /// Segment type, "ro" or "rw".
    /// </summary>
    public string Type { get; set; } = "";
    /// <summary>
    /// "zeropage" or "absolute".
    /// </summary>
    public string AddressSize { get; set; } = "";

    public bool IsWritable => Type == "rw";

    public static DebugSegment FromPairs(Dictionary<string, string> pairs)
    {
        var toReturn = new DebugSegment();
        if (pairs.TryGetValue("id", out var segmentId))
            toReturn.SegmentId = DebugFileParser.ParseNumber(segmentId);
        if (pairs.TryGetValue("name", out var name))
            toReturn.Name = name;
        if (pairs.TryGetValue("start", out var start))
            toReturn.Start = DebugFileParser.ParseNumber(start);
        if (pairs.TryGetValue("size", out var size))
            toReturn.Size = DebugFileParser.ParseNumber(size);
        if (pairs.TryGetValue("oname", out var outputFile))
            toReturn.OutputFile = outputFile;
        if (pairs.TryGetValue("ooffs", out var offset))
            toReturn.Offset = DebugFileParser.ParseNumber(offset);
        if (pairs.TryGetValue("type", out var type))
            toReturn.Type = type;
        if (pairs.TryGetValue("addrsize", out var addressSize))
            toReturn.AddressSize = addressSize;
        return toReturn;
    }
}

public enum DebugSymbolType
{
    Label,
    Equate,
    Import
}

public class DebugSymbol
{
    public int SymbolId { get; set; }
    public string Name { get; set; } = "";
    public DebugSymbolType Type { get; set; }
    public int Value { get; set; }
    /// <summary>
    /// Size of the symbol if known, eg from .res or the size of a scope.
    /// </summary>
    public int? Size { get; set; }
    /// <summary>
    /// "zeropage" or "absolute".
    /// </summary>
    public string AddressSize { get; set; } = "";
    public int ScopeId { get; set; } = -1;
    /// <summary>
    /// Parent symbol for cheap locals (@name).
    /// </summary>
    public int ParentId { get; set; } = -1;
    public int SegmentId { get; set; } = -1;

    public DebugScope? Scope { get; set; }
    public DebugSegment? Segment { get; set; }

    public static DebugSymbol FromPairs(Dictionary<string, string> pairs)
    {
        var toReturn = new DebugSymbol();
        if (pairs.TryGetValue("id", out var symbolId))
            toReturn.SymbolId = DebugFileParser.ParseNumber(symbolId);
        if (pairs.TryGetValue("name", out var name))
            toReturn.Name = name;
        if (pairs.TryGetValue("type", out var type))
            toReturn.Type = type switch
            {
                "lab" => DebugSymbolType.Label,
                "imp" => DebugSymbolType.Import,
                _ => DebugSymbolType.Equate
            };
        if (pairs.TryGetValue("val", out var value))
            toReturn.Value = DebugFileParser.ParseNumber(value);
        if (pairs.TryGetValue("size", out var size))
            toReturn.Size = DebugFileParser.ParseNumber(size);
        if (pairs.TryGetValue("addrsize", out var addressSize))
            toReturn.AddressSize = addressSize;
        if (pairs.TryGetValue("scope", out var scopeId))
            toReturn.ScopeId = DebugFileParser.ParseNumber(scopeId);
        if (pairs.TryGetValue("parent", out var parentId))
            toReturn.ParentId = DebugFileParser.ParseNumber(parentId);
        if (pairs.TryGetValue("seg", out var segmentId))
            toReturn.SegmentId = DebugFileParser.ParseNumber(segmentId);
        return toReturn;
    }
}
