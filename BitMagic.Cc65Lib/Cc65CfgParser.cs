using BitMagic.Compiler.CodingSeb;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace BitMagic.Cc65Lib;

public static class Cc65CfgParser
{
    private static Regex _removeComments = new Regex("/\\*(.|\\n)*?\\*/", RegexOptions.Multiline | RegexOptions.Compiled);
    private static Regex _removeLineComments = new Regex("#.*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static Regex _sections = new Regex("(?<name>\\w+)\\s+\\{(?<content>(\\n|\\r|\\r\\n|.)*?)\\}", RegexOptions.Multiline | RegexOptions.Compiled);
//    private static Regex _sectionValues = new Regex("\\s*(?<section>\\w+):\\s*((?<name>\\w+)\\s*=\\s*(?<value>\\w[\\S]|[^;,]+),?\\s*)*\\s*;", RegexOptions.Multiline | RegexOptions.Compiled);
    private static Regex _sectionValues = new Regex("\\s*(?<section>\\w+):\\s*((?<name>\\w+)\\s*=\\s*(?<value>(\"([^\"]*)\"|\\w+)|[^;,\\s]+)\\s*,?\\s*)*\\s*;", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly object _lock = new();
    private static Cc65Cfg? _returning;

    public static Cc65Cfg Parse(string filename, string defaultOutputPath, int? fileStartAddress)
    {
        lock (_lock)
        {
            var defaultOutputname = Path.GetFileName(defaultOutputPath);
            var contents = File.ReadAllText(filename);

            contents = _removeComments.Replace(contents, "");
            contents = _removeLineComments.Replace(contents, "");
            fileStartAddress ??= 0x801;

            var toReturn = new Cc65Cfg();
            _returning = toReturn;
            var evaluator = new Asm6502ExpressionEvaluator();
            evaluator.EvaluateVariable += Evaluator_EvaluateVariable;

            var sections = _sections.Matches(contents);

            if (sections == null)
                return toReturn;

            var memorySection = sections.FirstOrDefault(i => i.Groups["name"].ToString() == "MEMORY");
            var segmentSection = sections.FirstOrDefault(i => i.Groups["name"].ToString() == "SEGMENTS");
            var symbolsSection = sections.FirstOrDefault(i => i.Groups["name"].ToString() == "SYMBOLS");

            if (memorySection == null) return toReturn;
            if (segmentSection == null) return toReturn;

            MatchCollection? content = null;
            if (symbolsSection != null)
            {
                content = _sectionValues.Matches(symbolsSection.Groups["content"].ToString());
                if (content == null) return toReturn;

                foreach (Match match in content)
                {
                    var names = match.Groups["name"];
                    var value = match.Groups["value"];

                    for (var i = 0; i < names.Captures.Count; i++)
                    {
                        if (string.Equals(names.Captures[i].Value, "type", StringComparison.InvariantCultureIgnoreCase))
                        {
                            if (value.Captures[i].Value == "import")
                                toReturn.Imported.Add(match.Groups["section"].Value);

                            if (value.Captures[i].Value == "weak")
                            {
                                var s = evaluator.Evaluate(match.Groups["value"].Value);

                                var v = Convert.ToInt32(s);

                                toReturn.Symbols.Add(match.Groups["section"].Value, v);
                            }

                            break;
                        }
                    }
                }
            }

            content = _sectionValues.Matches(memorySection.Groups["content"].ToString());
            if (content == null) return toReturn;

            foreach (Match match in content)
            {
                var names = match.Groups["name"];
                var value = match.Groups["value"];
                var outputFile = (string?)null;
                var startAddress = (int?)null;
                var size = (int?)null;
                bool useS = false;

                for (var i = 0; i < names.Captures.Count; i++)
                {
                    if (string.Equals(names.Captures[i].Value, "file", StringComparison.InvariantCultureIgnoreCase))
                    {
                        outputFile = value.Captures[i].Value;
                        outputFile = outputFile.Replace("%O", defaultOutputname);
                    }
                    else if (string.Equals(names.Captures[i].Value, "start", StringComparison.InvariantCultureIgnoreCase))
                    {
                        var start = value.Captures[i].Value;

                        useS = start.Contains("%S");
                        start = start.Replace("%S", fileStartAddress.ToString());
                        start = start.Replace("__HEADER_LAST__", fileStartAddress.ToString());
                        start = start.Replace("__ONCE_RUN__", "$400");

                        var s = evaluator.Evaluate(start);

                        startAddress = Convert.ToInt32(s);
                    }
                    else if (string.Equals(names.Captures[i].Value, "size", StringComparison.CurrentCultureIgnoreCase))
                    {
                        var start = value.Captures[i].Value;

                        start = start.Replace("%S", fileStartAddress.ToString());
                        start = start.Replace("__HEADER_LAST__", fileStartAddress.ToString());
                        start = start.Replace("__ONCE_RUN__", "$400");

                        var s = evaluator.Evaluate(start);

                        size = Convert.ToInt32(s);
                    }
                }

                if (outputFile == null)
                    continue;

                if (outputFile == "\"\"")
                    continue;

                if (outputFile.StartsWith('"') && outputFile.EndsWith('"'))
                    outputFile = outputFile[1..^1];

                if (string.IsNullOrWhiteSpace(outputFile))
                    continue;

                if (!toReturn.Files.ContainsKey(outputFile))
                    toReturn.Files.Add(outputFile, new Cc65File() { Filename = outputFile, StartAddress = useS ? fileStartAddress : startAddress });

                var file = toReturn.Files[outputFile];

                var areaName = match.Groups["section"].Value;

                if (!file.Areas.ContainsKey(areaName))
                    file.Areas.Add(areaName, new Cc65MemoryArea() { Name = areaName, StartAddress = startAddress, Size = size });

                if (!toReturn.Areas.ContainsKey(areaName))
                    toReturn.Areas.Add(areaName, file.Areas[areaName]);
            }

            content = _sectionValues.Matches(segmentSection.Groups["content"].ToString());
            if (content == null) return toReturn;

            foreach (Match match in content)
            {
                var names = match.Groups["name"];
                var value = match.Groups["value"];

                var area = (string?)null;
                var areaType = (string?)null;
                var startAddress = (int?)null;
                bool optional = false;

                for (var i = 0; i < names.Captures.Count; i++)
                {
                    if (string.Equals(names.Captures[i].Value, "load", StringComparison.InvariantCultureIgnoreCase))
                    {
                        area = value.Captures[i].Value;
                    }
                    else if (string.Equals(names.Captures[i].Value, "type", StringComparison.InvariantCultureIgnoreCase))
                    {
                        areaType = value.Captures[i].Value;
                    }
                    else if (string.Equals(names.Captures[i].Value, "start", StringComparison.InvariantCultureIgnoreCase))
                    {
                        var start = value.Captures[i].Value;

                        start = start.Replace("%S", fileStartAddress.ToString());

                        var s = evaluator.Evaluate(start);

                        startAddress = Convert.ToInt32(s);
                    }
                    else if (string.Equals(names.Captures[i].Value, "optional", StringComparison.InvariantCultureIgnoreCase))
                    {
                        optional = string.Equals(value.Captures[i].Value, "yes", StringComparison.InvariantCultureIgnoreCase);
                    }
                }

                if (area == null || areaType == null)
                    continue;

                var segmentName = match.Groups["section"].Value;

                if (areaType == "zp")
                {
                    toReturn.Zp.Add(segmentName);
                    continue;
                }

                if (areaType != "ro" && areaType != "rw")
                    continue;

                if (!toReturn.Areas.ContainsKey(area))
                    continue;


                var memoryArea = toReturn.Areas[area];

                memoryArea.Segments.Add(segmentName, new Cc65Segment() { Name = area, StartAddress = startAddress, Optional = optional });
            }
            return toReturn;
        }
    }

    private static void Evaluator_EvaluateVariable(object? sender, CodingSeb.ExpressionEvaluator.VariableEvaluationEventArg e)
    {
        if (_returning == null) return;

        if (_returning.Symbols.ContainsKey(e.Name))
            e.Value = _returning.Symbols[e.Name];
    }
}

public class Cc65Cfg
{
    public Dictionary<string, Cc65File> Files { get; } = new();
    public Dictionary<string, Cc65MemoryArea> Areas { get; } = new();
    public HashSet<string> Imported { get; } = new();
    public HashSet<string> Zp { get; } = new();
    public Dictionary<string, int> Symbols { get; } = new();
}

public class Cc65File
{
    public string Filename { get; set; } = "";
    public Dictionary<string, Cc65MemoryArea> Areas { get; } = new();
    public int? StartAddress { get; set; } = null;
}

public class Cc65MemoryArea
{
    public string Name { get; set; } = "";
    public int? StartAddress { get; set; } = null;
    public int? Size { get; set; } = null;
    public Dictionary<string, Cc65Segment> Segments { get; } = new();
}

public class Cc65Segment
{
    public string Name { get; set; } = "";
    public int? StartAddress { get; set; } = null;
    public bool Optional { get; set; }
}