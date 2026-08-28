using System.Runtime.CompilerServices;

namespace BitMagic.Cc65Lib;

public static class DebugFileParser
{
    public static DebugInfo ParseFile(string filename)
    {
        var contents = File.ReadAllLines(filename);
        var toReturn = new DebugInfo();

        foreach (var line in contents.Skip(1))
        {
            var idx = line.IndexOf('\t');
            if (idx == -1) continue;

            var span = line.AsSpan();
            var key = span[..idx];

            if (SpanEquals(key, "line"))
            {
                toReturn.Lines.Add(DebugLine.FromPairs(GetPairs(span[(idx + 1)..])));
                continue;
            }

            if (SpanEquals(key, "file"))
            {
                toReturn.SourceFiles.Add(DebugSourceFile.FromPairs(GetPairs(span[(idx + 1)..])));
                continue;
            }

            if (SpanEquals(key, "mod"))
            {
                toReturn.Modules.Add(DebugModule.FromPairs(GetPairs(span[(idx + 1)..])));
                continue;
            }

            if (SpanEquals(key, "seg"))
            {
                toReturn.Segments.Add(DebugSegment.FromPairs(GetPairs(span[(idx + 1)..])));
                continue;
            }

            if (SpanEquals(key, "lib"))
            {
                toReturn.Libraries.Add(DebugLibrary.FromPairs(GetPairs(span[(idx + 1)..])));
                continue;
            }
        }

        foreach (var file in toReturn.SourceFiles)
        {
            var module = toReturn.Modules.Find(i => i.ModuleId == file.ModuleId);

            if (module == null) continue;

            module.SourceFiles.Add(file);
        }

        foreach (var line in toReturn.Lines)
        {
            var sourceFile = toReturn.SourceFiles.Find(i => i.FileId == line.FileId);

            if (sourceFile == null) continue;

            sourceFile.Lines.Add(line);
        }

        foreach(var module in toReturn.Modules)
        {
            module.MainSourceFile = module.SourceFiles.Find(i => i.FileId == module.FileId);
            module.LibrarySourceFile = toReturn.Libraries.Find(i => i.LibraryId == module.LibraryId);
        }

        return toReturn;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SpanEquals(ReadOnlySpan<char> span, string text)
    {
        return span.Equals(text.AsSpan(), StringComparison.Ordinal);
    }

    public static Dictionary<string, string> GetPairs(ReadOnlySpan<char> span)
    {
        var pairs = new Dictionary<string, string>();

        var lastIndex = 0;
        var equalsIndex = -1;

        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == '=')
                equalsIndex = i;

            if (span[i] == ',')
            {
                if (equalsIndex == -1)
                    continue;

                pairs.Add(span.Slice(lastIndex, equalsIndex - lastIndex).ToString(), span.Slice(equalsIndex + 1, i - equalsIndex - 1).ToString());
                lastIndex = i + 1;
                equalsIndex = -1;
            }
        }

        if (equalsIndex == -1)
            return pairs;

        pairs.Add(span.Slice(lastIndex, equalsIndex - lastIndex).ToString(), span.Slice(equalsIndex + 1).ToString());

        return pairs;
    }
}

public class DebugInfo
{
    public List<DebugSourceFile> SourceFiles { get; set; } = new();
    public List<DebugLine> Lines { get; set; } = new();
    public List<DebugModule> Modules { get; set; } = new();
    public List<DebugSegment> Segments { get; set; } = new();
    public List<DebugLibrary> Libraries { get; set; } = new();
}

public class DebugSourceFile
{
    public int FileId { get; set; }
    public string Name { get; set; } = "";
    public int Size { get; set; }
    public int ModuleId { get; set; }

    public List<DebugLine> Lines { get; set; } = [];

    public static DebugSourceFile FromPairs(Dictionary<string, string> pairs)
    {
        var toReturn = new DebugSourceFile();
        if (pairs.TryGetValue("id", out var fileId))
            toReturn.FileId = int.Parse(fileId);
        if (pairs.TryGetValue("name", out var name))
            toReturn.Name = name.Replace("\"", ""); ;
        if (pairs.TryGetValue("size", out var size))
            toReturn.Size = int.Parse(size);
        if (pairs.TryGetValue("mod", out var moduleId))
            toReturn.ModuleId = int.Parse(moduleId);
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
            toReturn.LibraryId = int.Parse(fileId);
        if (pairs.TryGetValue("name", out var name))
            toReturn.Name = name.Replace("\"", "");
        return toReturn;
    }
}

public class DebugLine
{
    public int LineId { get; set; }
    public int FileId { get; set; }
    public int LineNumber { get; set; }
    public int TypeId { get; set; }
    public int Count { get; set; }

    public static DebugLine FromPairs(Dictionary<string, string> pairs)
    {
        var toReturn = new DebugLine();
        if (pairs.TryGetValue("id", out var lineId))
            toReturn.LineId = int.Parse(lineId);
        if (pairs.TryGetValue("file", out var fileId))
            toReturn.FileId = int.Parse(fileId);
        if (pairs.TryGetValue("line", out var lineNumber))
            toReturn.LineNumber = int.Parse(lineNumber);
        if (pairs.TryGetValue("type", out var typeId))
            toReturn.TypeId = int.Parse(typeId);
        if (pairs.TryGetValue("count", out var count))
            toReturn.Count = int.Parse(count);
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

    public static DebugModule FromPairs(Dictionary<string, string> pairs)
    {
        var toReturn = new DebugModule();
        if (pairs.TryGetValue("id", out var moduleId))
            toReturn.ModuleId = int.Parse(moduleId);
        if (pairs.TryGetValue("name", out var name))
            toReturn.Name = name.Replace("\"", "");
        if (pairs.TryGetValue("file", out var fileId))
            toReturn.FileId = int.Parse(fileId);
        if (pairs.TryGetValue("lib", out var libraryId))
            toReturn.LibraryId = int.Parse(libraryId);
        return toReturn;
    }
}

public class DebugSegment
{
    public int SegmentId { get; set; }
    public string Name { get; set; } = "";
    public int Start { get; set; }
    public int Size { get; set; }
    public string OutputFile { get; set; } = "";
    public int Offset { get; set; }
    public int Bank { get; set; } = 0;

    public static DebugSegment FromPairs(Dictionary<string, string> pairs)
    {
        var toReturn = new DebugSegment();
        if (pairs.TryGetValue("id", out var segmentId))
            toReturn.SegmentId = int.Parse(segmentId);
        if (pairs.TryGetValue("name", out var name))
            toReturn.Name = name.Replace("\"", "");
        if (pairs.TryGetValue("start", out var start))
            toReturn.Start = Convert.ToInt32(start, 16);
        if (pairs.TryGetValue("size", out var size))
            toReturn.Size = Convert.ToInt32(size, 16);
        if (pairs.TryGetValue("oname", out var outputFile))
            toReturn.OutputFile = outputFile.Replace("\"", "");
        if (pairs.TryGetValue("ooffs", out var offset))
            toReturn.Offset = int.Parse(offset);
        if (pairs.TryGetValue("bank", out var bank))
            toReturn.Bank = int.Parse(bank);
        return toReturn;
    }
}

