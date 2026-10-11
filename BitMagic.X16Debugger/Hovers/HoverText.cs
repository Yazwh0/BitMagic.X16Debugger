using System.Text.RegularExpressions;

namespace BitMagic.X16Debugger.Hovers;

public enum HoverLineKind
{
    None,
    Instruction,
    Directive,
    Label
}

/// <summary>
/// What's under the cursor on a line of bmasm: the name, and the operand or expression around it.
/// Columns are zero based, ends are exclusive.
/// </summary>
public sealed class HoverTarget
{
    public HoverLineKind Kind { get; init; }

    /// <summary>The mnemonic or directive, lower case, without the '.'.</summary>
    public string Keyword { get; init; } = "";

    /// <summary>The name under the cursor, if it's on one that can be looked up.</summary>
    public string? Name { get; init; }
    public int NameStart { get; init; }
    public int NameEnd { get; init; }

    /// <summary>The operand as written (`#COUNT+1`, `lines,x`), or for a directive the value being hovered over.</summary>
    public string? Operand { get; init; }
    public int OperandStart { get; init; }
    public int OperandEnd { get; init; }

    /// <summary>The value part of the operand, without its addressing mode (`COUNT+1`, `lines`).</summary>
    public string? Expression { get; init; }

    /// <summary>The addressing mode around the expression: `#`, `,x`, `,y`, `(`, `)`, `,x)` and `),y`.</summary>
    public string Prefix { get; init; } = "";
    public string Suffix { get; init; } = "";

    /// <summary>For a directive that defines a name (`.const`, `.export`, `.var`), the name defined by the hovered item.</summary>
    public string? DefinedName { get; init; }
}

/// <summary>
/// Works out what's under the cursor on a line of bmasm. Shared by the language server's hover and the
/// debugger's, so both see the same name and expression.
/// </summary>
public static partial class HoverText
{
    // directives whose parameters are definitions: [public|private] [type [ptr]] name [=] value
    private static readonly HashSet<string> _definitions = new(StringComparer.OrdinalIgnoreCase) { "const", "export", "var", "constvar", "padvar" };
    private static readonly HashSet<string> _typed = new(StringComparer.OrdinalIgnoreCase) { "var", "constvar", "padvar" };

    // directives whose parameters are a list of values
    private static readonly HashSet<string> _values = new(StringComparer.OrdinalIgnoreCase) { "byte", "word" };

    [GeneratedRegex(@"^\.(?<name>[A-Za-z_]\w*):")]
    private static partial Regex LabelLine();

    // operand labels between the mnemonic and its operand: name: <lo: >hi:
    [GeneratedRegex(@"\G[<>]?[A-Za-z_]\w*:\s+")]
    private static partial Regex OperandLabel();

    [GeneratedRegex(@"^\((?<e>.*)\)\s*,\s*[yY]$")]
    private static partial Regex IndirectY();

    [GeneratedRegex(@"^\((?<e>.*?)\s*,\s*[xX]\s*\)$")]
    private static partial Regex IndirectX();

    [GeneratedRegex(@"^\((?<e>.*)\)$")]
    private static partial Regex Indirect();

    [GeneratedRegex(@"^(?<e>.*?)\s*,\s*(?<r>[xXyY])$")]
    private static partial Regex Indexed();

    public static HoverTarget? Find(string line, int column)
    {
        if (string.IsNullOrEmpty(line) || column < 0 || column > line.Length)
            return null;

        var code = line[..CommentStart(line)];

        if (column >= code.Length && !(column == code.Length && column > 0 && IsNameChar(code[column - 1])))
            return null;

        var start = 0;
        while (start < code.Length && char.IsWhiteSpace(code[start]))
            start++;

        if (start >= code.Length)
            return null;

        var label = LabelLine().Match(code[start..]);
        if (label.Success)
        {
            var nameStart = start + 1;
            var nameEnd = nameStart + label.Groups["name"].Length;

            if (column < nameStart - 1 || column > nameEnd)
                return null;

            return new HoverTarget
            {
                Kind = HoverLineKind.Label,
                Name = label.Groups["name"].Value,
                NameStart = nameStart,
                NameEnd = nameEnd,
                DefinedName = label.Groups["name"].Value
            };
        }

        // an anonymous label can share its line with an instruction
        if (code.AsSpan(start).StartsWith(".:"))
        {
            start += 2;
            while (start < code.Length && char.IsWhiteSpace(code[start]))
                start++;
        }
        else if (code[start] == '.')
        {
            return FindInDirective(code, start, column);
        }

        return FindInInstruction(code, start, column);
    }

    private static HoverTarget? FindInInstruction(string code, int start, int column)
    {
        var keywordEnd = start;
        while (keywordEnd < code.Length && !char.IsWhiteSpace(code[keywordEnd]))
            keywordEnd++;

        var keyword = code[start..keywordEnd].ToLowerInvariant();

        var operandStart = keywordEnd;
        while (operandStart < code.Length && char.IsWhiteSpace(code[operandStart]))
            operandStart++;

        // step past any operand labels
        var labels = OperandLabel().Match(code, operandStart);
        while (labels.Success && labels.Length > 0)
        {
            operandStart += labels.Length;
            labels = OperandLabel().Match(code, operandStart);
        }

        var operandEnd = code.Length;
        while (operandEnd > operandStart && char.IsWhiteSpace(code[operandEnd - 1]))
            operandEnd--;

        if (column < operandStart || column > operandEnd || operandStart >= operandEnd)
            return null;

        var operand = code[operandStart..operandEnd];
        var (prefix, expression, suffix, expressionStart) = SplitAddressing(operand);
        expressionStart += operandStart;

        var (name, nameStart, nameEnd) = NameAt(code, column, expressionStart, expressionStart + expression.Length);

        return new HoverTarget
        {
            Kind = HoverLineKind.Instruction,
            Keyword = keyword,
            Name = name,
            NameStart = nameStart,
            NameEnd = nameEnd,
            Operand = operand,
            OperandStart = operandStart,
            OperandEnd = operandEnd,
            Expression = expression,
            Prefix = prefix,
            Suffix = suffix
        };
    }

    private static HoverTarget? FindInDirective(string code, int start, int column)
    {
        var keywordEnd = start + 1;
        while (keywordEnd < code.Length && !char.IsWhiteSpace(code[keywordEnd]))
            keywordEnd++;

        var keyword = code[(start + 1)..keywordEnd].ToLowerInvariant();

        if (column < keywordEnd)
            return null;

        if (_values.Contains(keyword))
        {
            foreach (var (itemStart, itemEnd) in Items(code, keywordEnd, code.Length))
            {
                if (column < itemStart || column > itemEnd)
                    continue;

                var (name, nameStart, nameEnd) = NameAt(code, column, itemStart, itemEnd);
                var item = code[itemStart..itemEnd];

                return new HoverTarget
                {
                    Kind = HoverLineKind.Directive,
                    Keyword = keyword,
                    Name = name,
                    NameStart = nameStart,
                    NameEnd = nameEnd,
                    Operand = item,
                    OperandStart = itemStart,
                    OperandEnd = itemEnd,
                    Expression = item
                };
            }

            return null;
        }

        if (!_definitions.Contains(keyword))
            return null;

        // .const can define several on a line, each of the others defines one
        var items = keyword == "const" ? Items(code, keywordEnd, code.Length) : [(Skip(code, keywordEnd), TrimEnd(code, code.Length))];

        foreach (var (itemStart, itemEnd) in items)
        {
            if (column < itemStart || column > itemEnd)
                continue;

            var pos = itemStart;
            var first = true;

            // [public|private] [type [ptr]]
            while (true)
            {
                var (wordStart, wordEnd) = Word(code, pos, itemEnd);
                var word = code[wordStart..wordEnd];

                if (first && (word == "public" || word == "private"))
                {
                    pos = wordEnd;
                    first = false;
                    continue;
                }

                first = false;

                if (_typed.Contains(keyword))
                {
                    pos = wordEnd; // the type
                    var (ptrStart, ptrEnd) = Word(code, pos, itemEnd);
                    if (code[ptrStart..ptrEnd] == "ptr")
                        pos = ptrEnd;
                }

                break;
            }

            var (definedStart, definedEnd) = NameWord(code, Skip(code, pos), itemEnd);
            var definedName = code[definedStart..definedEnd];

            var valueStart = Skip(code, definedEnd);
            if (valueStart < itemEnd && code[valueStart] == '=')
                valueStart = Skip(code, valueStart + 1);

            if (column >= definedStart && column <= definedEnd && definedEnd > definedStart)
            {
                return new HoverTarget
                {
                    Kind = HoverLineKind.Directive,
                    Keyword = keyword,
                    Name = definedName,
                    NameStart = definedStart,
                    NameEnd = definedEnd,
                    DefinedName = definedName,
                    Operand = valueStart < itemEnd ? code[valueStart..itemEnd] : null,
                    OperandStart = valueStart,
                    OperandEnd = itemEnd,
                    Expression = valueStart < itemEnd ? code[valueStart..itemEnd] : null
                };
            }

            if (column < valueStart || valueStart >= itemEnd)
                return null;

            var (name, nameStart, nameEnd) = NameAt(code, column, valueStart, itemEnd);

            return new HoverTarget
            {
                Kind = HoverLineKind.Directive,
                Keyword = keyword,
                Name = name,
                NameStart = nameStart,
                NameEnd = nameEnd,
                DefinedName = definedName,
                Operand = code[valueStart..itemEnd],
                OperandStart = valueStart,
                OperandEnd = itemEnd,
                Expression = code[valueStart..itemEnd]
            };
        }

        return null;
    }

    private static (string Prefix, string Expression, string Suffix, int ExpressionStart) SplitAddressing(string operand)
    {
        if (operand.StartsWith('#'))
        {
            var start = Skip(operand, 1);
            return ("#", operand[start..], "", start);
        }

        foreach (var (regex, prefix, suffix) in new[] { (IndirectY(), "(", "),y"), (IndirectX(), "(", ",x)"), (Indirect(), "(", ")") })
        {
            var match = regex.Match(operand);
            if (match.Success)
            {
                var e = match.Groups["e"];
                var start = Skip(operand, e.Index);
                return (prefix, operand[start..TrimEnd(operand, e.Index + e.Length)], suffix, start);
            }
        }

        var indexed = Indexed().Match(operand);
        if (indexed.Success)
            return ("", indexed.Groups["e"].Value, "," + indexed.Groups["r"].Value.ToLowerInvariant(), 0);

        return ("", operand, "", 0);
    }

    /// <summary>
    /// The name at the column, if it's within start..end: letters, digits, '_' and ':', with any ':' at
    /// either end left off. Numbers ($ff, %1010, 12) and character literals give no name.
    /// </summary>
    public static (string? Name, int Start, int End) NameAt(string text, int column, int start, int end)
    {
        if (column < start || column > end)
            return (null, 0, 0);

        // allow the cursor just after the last character
        if (column == end || !IsNameChar(text[column]))
        {
            if (column > start && IsNameChar(text[column - 1]))
                column--;
            else
                return (null, 0, 0);
        }

        var nameStart = column;
        while (nameStart > start && IsNameChar(text[nameStart - 1]))
            nameStart--;

        var nameEnd = column + 1;
        while (nameEnd < end && IsNameChar(text[nameEnd]))
            nameEnd++;

        while (nameStart < nameEnd && text[nameStart] == ':')
            nameStart++;

        while (nameEnd > nameStart && text[nameEnd - 1] == ':')
            nameEnd--;

        if (nameStart >= nameEnd || !(char.IsLetter(text[nameStart]) || text[nameStart] == '_'))
            return (null, 0, 0);

        // a number or a character literal
        if (nameStart > 0 && text[nameStart - 1] is '$' or '%' or '\'')
            return (null, 0, 0);

        return (text[nameStart..nameEnd], nameStart, nameEnd);
    }

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == ':';

    // where the comment starts, ignoring a ';' in a string or character literal
    private static int CommentStart(string line)
    {
        var quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
                continue;
            }

            if (c is '"' or '\'')
                quote = c;
            else if (c == ';' || (c == '/' && i + 1 < line.Length && line[i + 1] == '/'))
                return i;
        }

        return line.Length;
    }

    // the comma separated items in start..end, outside brackets and quotes, trimmed
    private static List<(int Start, int End)> Items(string text, int start, int end)
    {
        var items = new List<(int, int)>();
        var depth = 0;
        var quote = '\0';
        var itemStart = start;

        for (var i = start; i <= end; i++)
        {
            if (i == end || (text[i] == ',' && depth == 0 && quote == '\0'))
            {
                var s = Skip(text, itemStart);
                var e = TrimEnd(text, i);
                if (e > s)
                    items.Add((s, e));
                itemStart = i + 1;
                continue;
            }

            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
            }
            else if (c is '"' or '\'')
                quote = c;
            else if (c is '(' or '[')
                depth++;
            else if (c is ')' or ']')
                depth--;
        }

        return items;
    }

    private static (int Start, int End) Word(string text, int pos, int end)
    {
        var s = Skip(text, pos);
        var e = s;
        while (e < end && !char.IsWhiteSpace(text[e]) && text[e] != '=')
            e++;
        return (s, e);
    }

    private static (int Start, int End) NameWord(string text, int pos, int end)
    {
        var e = pos;
        while (e < end && (char.IsLetterOrDigit(text[e]) || text[e] == '_'))
            e++;
        return (pos, e);
    }

    private static int Skip(string text, int pos)
    {
        while (pos < text.Length && char.IsWhiteSpace(text[pos]))
            pos++;
        return pos;
    }

    private static int TrimEnd(string text, int end)
    {
        while (end > 0 && char.IsWhiteSpace(text[end - 1]))
            end--;
        return end;
    }
}
