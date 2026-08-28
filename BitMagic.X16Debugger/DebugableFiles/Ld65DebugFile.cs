using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BitMagic.X16Debugger.DebugableFiles;

internal class Ld65DebugFile
{
    // Represents a binary output produced by ld65 (for example a .bin/.prg)
    public class BinaryFile
    {
        public string Filename { get; init; } = "";
        // Segments or mappings inside the binary (address and size refer to target memory addresses)
        public List<SegmentMapping> Mappings { get; } = new();
        // Source files referenced by this binary (collected from FILE records)
        public HashSet<string> SourceFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    // A mapping between a range in target memory and a source file / line (if known)
    public class SegmentMapping
    {
        public uint Address { get; init; }
        public uint Size { get; init; }
        public string? SourceFile { get; init; }
        public int? SourceLine { get; init; }

        public override string ToString()
            => $"{Address:X4}..{(Address + Size - 1):X4} ({Size}) => {SourceFile ?? "<unknown>"}:{SourceLine?.ToString() ?? "-"}";
    }

    // Collection of binaries parsed from the debug file
    public List<BinaryFile> Binaries { get; } = new();

    // Load and parse a ld65 --debugfile output.
    // The parser is tolerant: it recognises common tokens emitted by ld65 (FILE, SEGMENT, MAP, BIN/BINARY/OUTPUT),
    // but will also try to detect quoted filenames and basic address/size patterns if the exact token differs.
    public static Ld65DebugFile Load(string filename)
    {
        var text = File.ReadAllText(filename);
        return Parse(text);
    }

    public static Ld65DebugFile Parse(string debugFileContents)
    {
        var result = new Ld65DebugFile();

        // Map of file-id -> source path (ld65 often emits "FILE <id> <path>")
        var fileIdToPath = new Dictionary<int, string>();
        BinaryFile? currentBinary = null;

        var lines = debugFileContents
            .Replace("\r\n", "\n")
            .Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            var tokens = Tokenize(line);
            if (tokens.Count == 0)
                continue;

            var head = tokens[0].ToUpperInvariant();

            // Handle common explicit tokens
            switch (head)
            {
                case "FILE":
                    // FILE <id> "<path>"  or FILE <id> path
                    if (tokens.Count >= 3 && int.TryParse(tokens[1], out var fid))
                    {
                        fileIdToPath[fid] = StripQuotes(tokens[2]);
                    }
                    break;

                case "BIN":
                case "BINARY":
                case "OUTPUT":
                case "OUTPUTFILE":
                case "OUTPUT_FILE":
                    if (tokens.Count >= 2)
                    {
                        var binName = StripQuotes(tokens[1]);
                        currentBinary = new BinaryFile { Filename = binName };
                        result.Binaries.Add(currentBinary);
                    }
                    break;

                case "SEGMENT":
                case "SEG":
                    // SEGMENT <name> <start> <size> [<fileid> [<line>]]
                    if (tokens.Count >= 4 && currentBinary != null)
                    {
                        var start = ParseNumber(tokens[2]);
                        var size = ParseNumber(tokens[3]);
                        string? src = null;
                        int? lineNo = null;
                        if (tokens.Count >= 5 && int.TryParse(tokens[4], out var fId) && fileIdToPath.TryGetValue(fId, out var path))
                        {
                            src = path;
                            currentBinary.SourceFiles.Add(path);
                        }
                        if (tokens.Count >= 6 && int.TryParse(tokens[5], out var ln))
                            lineNo = ln;

                        currentBinary.Mappings.Add(new SegmentMapping
                        {
                            Address = start,
                            Size = size,
                            SourceFile = src,
                            SourceLine = lineNo
                        });
                    }
                    break;

                case "MAP":
                    // MAP <start> <endOrSize> <fileid> <line>
                    if (tokens.Count >= 3 && currentBinary != null)
                    {
                        var start = ParseNumber(tokens[1]);
                        var second = ParseNumber(tokens[2]);
                        uint size = second;
                        // if looks like end (greater than start) treat as end
                        if (second > start)
                            size = second - start + 1;

                        string? src = null;
                        int? lineNo = null;
                        if (tokens.Count >= 4 && int.TryParse(tokens[3], out var fId) && fileIdToPath.TryGetValue(fId, out var path))
                        {
                            src = path;
                            currentBinary.SourceFiles.Add(path);
                        }

                        if (tokens.Count >= 5 && int.TryParse(tokens[4], out var ln))
                            lineNo = ln;

                        currentBinary.Mappings.Add(new SegmentMapping
                        {
                            Address = start,
                            Size = size,
                            SourceFile = src,
                            SourceLine = lineNo
                        });
                    }
                    break;

                default:
                    // Heuristics:
                    // 1) If the line is a quoted filename or looks like an output file path (.prg/.bin/.x16/.rom) start a new binary section.
                    // 2) If tokens contain an integer file-id as the first token and a quoted filename as the second, treat as FILE.
                    // 3) If tokens look like "0x0801 0x.. <id> <line>" treat as a mapping.
                    if (IsLikelyFilename(head))
                    {
                        // Start a new binary
                        currentBinary = new BinaryFile { Filename = StripQuotes(tokens[0]) };
                        result.Binaries.Add(currentBinary);
                        continue;
                    }

                    // pattern: <id> "<path>"  -> file entry
                    if (tokens.Count >= 2 && int.TryParse(tokens[0], out var id2) && IsLikelyFilename(tokens[1]))
                    {
                        fileIdToPath[id2] = StripQuotes(tokens[1]);
                        continue;
                    }

                    // pattern: <start> <endOrSize> <fileid> <line>
                    if (tokens.Count >= 3 && LooksLikeNumber(tokens[0]) && LooksLikeNumber(tokens[1]) && currentBinary != null)
                    {
                        var start = ParseNumber(tokens[0]);
                        var second = ParseNumber(tokens[1]);
                        uint size = second;
                        if (second > start)
                            size = second - start + 1;

                        string? src = null;
                        int? lineNo = null;
                        if (tokens.Count >= 3 && int.TryParse(tokens[2], out var fId2) && fileIdToPath.TryGetValue(fId2, out var p2))
                        {
                            src = p2;
                            currentBinary.SourceFiles.Add(p2);
                        }
                        if (tokens.Count >= 4 && int.TryParse(tokens[3], out var ln2))
                            lineNo = ln2;

                        currentBinary.Mappings.Add(new SegmentMapping
                        {
                            Address = start,
                            Size = size,
                            SourceFile = src,
                            SourceLine = lineNo
                        });
                        continue;
                    }

                    // nothing recognised - skip line
                    break;
            }
        }

        return result;
    }

    // Tokenize a line, preserving quoted strings as single tokens
    private static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        // matches double-quoted strings OR consecutive non-whitespace characters
        var rx = new Regex("\"([^\"]*)\"|([^\\s]+)", RegexOptions.Compiled);
        foreach (Match m in rx.Matches(line))
        {
            if (m.Groups[1].Success)
                tokens.Add(m.Groups[1].Value);
            else
                tokens.Add(m.Groups[2].Value);
        }
        return tokens;
    }

    private static string StripQuotes(string s)
    {
        if (s == null) return "";
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            return s.Substring(1, s.Length - 2);
        return s;
    }

    private static bool IsLikelyFilename(string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        token = StripQuotes(token);
        var lower = token.ToLowerInvariant();
        return lower.EndsWith(".bin") || lower.EndsWith(".prg") || lower.EndsWith(".x16") || lower.EndsWith(".rom") || lower.Contains('\\') || lower.Contains('/');
    }

    private static bool LooksLikeNumber(string s)
    {
        s = s.Trim();
        if (s.StartsWith("$")) return true;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return true;
        return int.TryParse(s, out _) || uint.TryParse(s, out _);
    }

    // Parse number allowing $hex, 0xhex or decimal
    private static uint ParseNumber(string token)
    {
        token = token.Trim();
        if (string.IsNullOrEmpty(token))
            return 0;

        // Remove surrounding quotes if present
        token = StripQuotes(token);

        if (token.StartsWith("$"))
        {
            if (uint.TryParse(token.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                return v;
        }
        else if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (uint.TryParse(token.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                return v;
        }
        else
        {
            // Try hex without prefix (common in some outputs)
            if (Regex.IsMatch(token, @"^[0-9A-Fa-f]+$") && token.Length > 1)
            {
                if (uint.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v2))
                    return v2;
            }

            if (uint.TryParse(token, out var v3))
                return v3;
        }

        return 0;
    }
}
