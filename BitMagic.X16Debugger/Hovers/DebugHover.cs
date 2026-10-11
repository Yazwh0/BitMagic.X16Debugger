using BitMagic.Common;
using BitMagic.X16Debugger.Scopes;
using BitMagic.X16Debugger.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;

namespace BitMagic.X16Debugger.Hovers;

/// <summary>
/// The debugger's hover: the same as the language server's, from the build being debugged, with live values.
/// VSCode shows a debug hover as a value that can be expanded, so the breakdown is its children.
/// </summary>
internal sealed class DebugHover(ServiceManager serviceManager)
{
    // the build this session is running, taken at launch: the language server may rebuild since
    private HoverSnapshot? _snapshot;

    // one set of children, replaced by each hover, so hovers don't pile up variables
    private VariableIndex? _children;
    private List<Variable> _current = new();

    public void SetSnapshot(HoverSnapshot? snapshot) => _snapshot = snapshot;

    /// <summary>
    /// The hover for an evaluate request, or null if it can't be worked out from the source (no position, or the
    /// file has changed since the build), when the caller should evaluate it as an expression instead.
    /// </summary>
    public EvaluateResponse? Evaluate(EvaluateArguments arguments)
    {
        if (_snapshot == null || !TryGetPosition(arguments, out var path, out var line, out var column))
            return null;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (line < 0 || line >= lines.Length)
            return null;

        var lineText = lines[line];

        // the file has changed since VSCode found the expression
        if (column < 0 || column > lineText.Length || !lineText[column..].StartsWith(arguments.Expression))
            return null;

        var hover = _snapshot.Hover(path, line, lineText, column, new EmulatorHoverRuntime(serviceManager.Emulator));
        if (hover == null)
            return null;

        return ToResponse(hover.Value.Result);
    }

    private EvaluateResponse ToResponse(HoverResult result)
    {
        var children = new List<Variable>();
        string value;
        string type;

        if (result.Operand is HoverOperand operand)
        {
            var loaded = operand.Effective.FirstOrDefault(i => i.Name == "value").Value;

            value = operand.Problem != null ? $"{operand.Text} {operand.Problem}" : loaded ?? operand.Value?.ToString() ?? "";
            type = operand.Mode ?? "";

            if (result.Name is HoverName hovered && hovered.FullName != null)
                children.Add(Child(hovered.FullName, hovered.Live ?? hovered.Value?.ToString() ?? hovered.Message ?? "", hovered.Type));

            if (loaded != null && operand.Value is HoverNumber number)
                children.Add(Child("operand", number.ToString(), operand.Mode));

            foreach (var i in operand.Names)
                children.Add(Child(i.Name, i.Value?.ToString() ?? "", $"{i.FullName}, {i.Type}"));

            if (operand.Substituted != null)
                children.Add(Child("expression", operand.Substituted));

            foreach (var (name, text) in operand.Effective.Where(i => i.Name != "value"))
                children.Add(Child(name, text));
        }
        else if (result.Name is HoverName name)
        {
            value = name.Live ?? name.Value?.ToString() ?? name.Message ?? "";
            type = name.Type ?? "";

            if (name.FullName != null)
                children.Add(Child("name", name.FullName));

            if (name.Live != null && name.Value is HoverNumber address)
                children.Add(Child("address", address.Hex));

            if (name.Note != null)
                children.Add(Child("note", name.Note));

            if (name.ExportOf != null)
                children.Add(Child("export of", name.ExportOf));
        }
        else
        {
            value = "";
            type = "";
        }

        if (result.GeneratedLine != null)
            children.Add(Child("compiles as", result.GeneratedLine.Trim()));

        _current = children;
        _children ??= serviceManager.VariableManager.Register(new VariableIndex("hover", () => ("", _current)));

        return new EvaluateResponse
        {
            Result = value.Replace("`", ""),
            Type = type,
            VariablesReference = children.Count > 0 ? _children.Id : 0,
            NamedVariables = children.Count
        };
    }

    private static Variable Child(string name, string value, string? type = null) => new()
    {
        Name = name,
        Value = value.Replace("`", ""),
        Type = type ?? "",
        PresentationHint = new VariablePresentationHint { Attributes = VariablePresentationHint.AttributesValue.ReadOnly }
    };

    // VSCode sends where the hover is (DAP's source, line and column on evaluate), which this version of the
    // protocol library doesn't know, so they're kept in its additional properties, which aren't public.
    private static readonly System.Reflection.PropertyInfo? _additionalProperties =
        typeof(EvaluateArguments).GetProperty("AdditionalProperties", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

    // Lines and columns start at 1.
    private static bool TryGetPosition(EvaluateArguments arguments, out string path, out int line, out int column)
    {
        path = "";
        line = 0;
        column = 0;

        if (_additionalProperties?.GetValue(arguments) is not System.Collections.IDictionary dictionary)
            return false;

        var extra = dictionary.Keys.Cast<object>().ToDictionary(i => i.ToString()!, i => dictionary[i]);
        if (
            !extra.TryGetValue("source", out var source) ||
            !extra.TryGetValue("line", out var lineValue) ||
            !extra.TryGetValue("column", out var columnValue))
            return false;

        try
        {
            var sourcePath = ToToken(source)?["path"]?.ToString();
            if (string.IsNullOrWhiteSpace(sourcePath))
                return false;

            path = sourcePath.FixFilename();
            line = (int)ToToken(lineValue)! - 1;
            column = (int)ToToken(columnValue)! - 1;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static JToken? ToToken(object? value) => value as JToken ?? (value == null ? null : JToken.FromObject(value));
}

/// <summary>
/// Reads the emulator for the hover, never through the CPU's read path, so nothing it looks at changes.
/// </summary>
internal sealed class EmulatorHoverRuntime(Emulator emulator) : IHoverRuntime
{
    public byte X => emulator.X;
    public byte Y => emulator.Y;

    private static bool IsIo(int address) => address is >= 0x9f00 and < 0xa000;

    public byte? Peek(int address)
    {
        address &= 0xffff;

        if (IsIo(address))
            return null;

        if (address >= 0xc000)
        {
            var offset = (int)emulator.RomBankAct * 0x4000 + (address - 0xc000);
            return offset < Emulator.RomSize ? emulator.RomBank[offset] : null;
        }

        if (address >= 0xa000)
        {
            var offset = (int)emulator.RamBankAct * 0x2000 + (address - 0xa000);
            return offset < Emulator.BankedRamSize ? emulator.RamBank[offset] : null;
        }

        return emulator.Memory[address];
    }

    public string? AddressNote(int address)
    {
        address &= 0xffff;

        if (IsIo(address))
            return "IO, not read";

        if (address >= 0xc000)
            return $"ROM bank {emulator.RomBankAct}";

        if (address >= 0xa000)
            return $"RAM bank {emulator.RamBankAct}";

        return null;
    }

    public string? Describe(IAsmVariable variable)
    {
        if (variable.Array || variable is DebuggerVariable)
            return null;

        var address = variable.Value;

        int? Read(int size)
        {
            var value = 0;
            for (var i = 0; i < size; i++)
            {
                if (Peek(address + i) is not byte b)
                    return null;
                value |= b << (8 * i);
            }
            return value;
        }

        return variable.VariableDataType switch
        {
            VariableDataType.Byte or VariableDataType.Char => Read(1) is int b ? HoverNumber.Of(b).ToString() : null,
            VariableDataType.Sbyte => Read(1) is int sb ? $"{HoverNumber.Of(sb).Hex} ({(sbyte)sb})" : null,
            VariableDataType.Ushort => Read(2) is int us ? new HoverNumber(us, true).ToString() : null,
            VariableDataType.Short => Read(2) is int s ? $"{new HoverNumber(s, true).Hex} ({(short)s})" : null,
            VariableDataType.Uint or VariableDataType.Int => Read(4) is int i ? $"${i:X8} ({(variable.VariableDataType == VariableDataType.Int ? i : (uint)i)})" : null,
            VariableDataType.Ptr or VariableDataType.BytePtr or VariableDataType.SbytePtr or VariableDataType.CharPtr or
            VariableDataType.ShortPtr or VariableDataType.UshortPtr or VariableDataType.IntPtr or VariableDataType.UintPtr or
            VariableDataType.LongPtr or VariableDataType.UlongPtr or VariableDataType.StringPtr or VariableDataType.FixedStringsPtr
                => Read(2) is int p ? $"-> {new HoverNumber(p, true).Hex}" : null,
            _ => null
        };
    }
}
