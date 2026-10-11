using BitMagic.Common;
using BitMagic.Compiler;
using System.Text;
using System.Text.RegularExpressions;
using CompilerVariables = BitMagic.Compiler.Variables;

namespace BitMagic.X16Debugger.Hovers;

/// <summary>
/// Live machine state for the debugger's hover. The language server has none, so shows build-time values only.
/// </summary>
public interface IHoverRuntime
{
    byte X { get; }
    byte Y { get; }

    /// <summary>The value of a name at run time, eg a variable's current contents, or null to show its build-time value.</summary>
    string? Describe(IAsmVariable variable);

    /// <summary>A byte of memory as the CPU would see it now, read without side effects; null where that isn't safe (IO).</summary>
    byte? Peek(int address);

    /// <summary>A note for an address, eg the bank it's in, or why its value isn't shown.</summary>
    string? AddressNote(int address);
}

/// <summary>A number for the hover. An address is only shown in hex; a value has its decimal too.</summary>
public readonly record struct HoverNumber(int Value, bool Word, bool Address = false)
{
    public static HoverNumber Of(int value) => new(value, value is < 0 or > 0xff);

    public static HoverNumber OfAddress(int value) => new(value, value is < 0 or > 0xff, true);

    public string Hex => Word ? $"${Value & 0xffff:X4}" : $"${Value & 0xff:X2}";

    public override string ToString() => Address ? Hex : $"{Hex} ({Value})";

    public string Markdown => Address ? $"`{Hex}`" : $"`{Hex}` ({Value})";
}

/// <summary>A name: what it is and its value.</summary>
public sealed class HoverName
{
    public required string Name { get; init; }
    public string? FullName { get; init; }
    public string? Type { get; init; }
    public HoverNumber? Value { get; init; }

    /// <summary>When the name can't be shown: not defined, not known yet, or a debugger alias.</summary>
    public string? Message { get; init; }

    /// <summary>Its value now, from the debugger.</summary>
    public string? Live { get; init; }

    /// <summary>About its address, eg the bank.</summary>
    public string? Note { get; init; }

    public string? ExportOf { get; init; }
}

/// <summary>An operand or expression: what it works out to, and how.</summary>
public sealed class HoverOperand
{
    public required string Text { get; init; }
    public HoverNumber? Value { get; set; }

    /// <summary>The addressing mode, or for a branch where it goes.</summary>
    public string? Mode { get; set; }

    /// <summary>Why it can't be worked out.</summary>
    public string? Problem { get; set; }

    /// <summary>The names in it, with their values.</summary>
    public List<HoverName> Names { get; } = new();

    /// <summary>The expression with the values put in.</summary>
    public string? Substituted { get; set; }

    /// <summary>For an indexed or indirect operand, where it ends up: the registers, the address and what's there.</summary>
    public List<(string Name, string Value)> Effective { get; } = new();
}

public sealed class HoverResult
{
    public HoverName? Name { get; set; }
    public HoverOperand? Operand { get; set; }

    /// <summary>The line as compiled, when it differs from what's shown (template code).</summary>
    public string? GeneratedLine { get; set; }

    public string ToMarkdown()
    {
        var sb = new StringBuilder();

        if (Name != null)
        {
            if (Name.Message != null && Name.FullName == null)
            {
                sb.Append(Name.Message);
            }
            else
            {
                sb.Append($"**{Name.FullName}**  \n{Name.Type}  \n");

                if (Name.Message != null)
                    sb.Append(Name.Message);
                else if (Name.Value is HoverNumber value)
                    sb.Append(value.Markdown);

                if (Name.Note != null)
                    sb.Append($" ({Name.Note})");

                if (Name.Live != null)
                    sb.Append($"  \nNow `{Name.Live}`");

                if (Name.ExportOf != null)
                    sb.Append($"  \nExport of `{Name.ExportOf}`");
            }
        }

        if (Operand != null)
        {
            Separate(sb);

            if (Operand.Problem != null)
                sb.Append($"`{Operand.Text}` {Operand.Problem}");
            else
                sb.Append($"`{Operand.Text}` = {Operand.Value?.Markdown}{(Operand.Mode != null ? $", {Operand.Mode}" : "")}");

            foreach (var name in Operand.Names)
                sb.Append($"  \n`{name.Name}` {name.FullName}, {name.Type}, {name.Value?.Markdown}");

            if (Operand.Substituted != null)
                sb.Append($"  \n`{Operand.Substituted}`");

            if (Operand.Effective.Count > 0)
                sb.Append("  \n" + string.Join(", ", Operand.Effective.Select(i => $"{i.Name} {i.Value}")));
        }

        if (GeneratedLine != null)
        {
            Separate(sb);
            sb.Append($"Compiles as `{GeneratedLine.Trim()}`");
        }

        return sb.ToString();
    }

    private static void Separate(StringBuilder sb)
    {
        if (sb.Length > 0)
            sb.Append("\n\n");
    }
}

/// <summary>
/// Builds the hover for a line of bmasm: what the name under the cursor is, and what the operand or expression
/// around it works out to, with the values of the names in it.
/// </summary>
public static partial class HoverBuilder
{
    [GeneratedRegex(@"(?<![\w$%'.:])(?<name>:?[A-Za-z_]\w*(?::{1,2}[A-Za-z_]\w*)*)")]
    private static partial Regex Names();

    public sealed class Context
    {
        public required CompileState State { get; init; }

        /// <summary>The procedure the line is in, which names are looked up from.</summary>
        public required Procedure Procedure { get; init; }

        /// <summary>The compiled instruction or data for the line, if it produced any.</summary>
        public IOutputData? Output { get; init; }

        /// <summary>The line as compiled, when it differs from what's shown (template code).</summary>
        public string? GeneratedLine { get; init; }

        public IHoverRuntime? Runtime { get; init; }
    }

    public static HoverResult? Build(HoverTarget target, Context context)
    {
        var result = new HoverResult { GeneratedLine = context.GeneratedLine };

        if (target.Name != null)
            result.Name = DescribeName(target.Name, context);

        var showExpression = target.Expression != null &&
            (target.Name == null || target.Expression != target.Name || target.Prefix != "" || target.Suffix != "" || target.DefinedName == target.Name);

        if (showExpression)
        {
            result.Operand = target.Kind == HoverLineKind.Instruction ?
                DescribeOperand(target, context) :
                DescribeExpression(target.Expression!, context);
        }

        return result.Name == null && result.Operand == null && result.GeneratedLine == null ? null : result;
    }

    private static HoverName DescribeName(string name, Context context)
    {
        if (!Lookup(name, context, out var variable, out var owner))
            return new HoverName { Name = name, Message = $"`{name}` isn't defined here." };

        var type = TypeName(variable);

        if (variable is AsmVariable asm && asm.Visibility == Visibility.Private)
            type += ", private";
        else if (variable is AsmVariable { IsLabel: true })
            type += ", private outside its scope";

        if (variable is DebuggerVariable debuggerVariable)
            return new HoverName { Name = name, FullName = FullName(variable, owner), Type = type, Message = $"Debugger alias for `{debuggerVariable.Expression}`" };

        if (variable.RequiresReval)
            return new HoverName { Name = name, FullName = FullName(variable, owner), Type = type, Message = "Its value isn't known yet." };

        return new HoverName
        {
            Name = name,
            FullName = FullName(variable, owner),
            Type = type,
            Value = IsAddress(variable) ? HoverNumber.OfAddress(variable.Value) : HoverNumber.Of(variable.Value),
            Note = IsAddress(variable) ? context.Runtime?.AddressNote(variable.Value) : null,
            Live = context.Runtime?.Describe(variable),
            ExportOf = variable is AsmVariable { AliasOf: not null } alias ? alias.AliasOf : null
        };
    }

    private static HoverOperand DescribeOperand(HoverTarget target, Context context)
    {
        if (context.Output is not Line line || line.Data.Length < 2)
        {
            // not compiled, so work it out as the compiler would
            return DescribeExpression(target.Expression!, context, target.Operand);
        }

        var operand = 0;
        for (var i = 1; i < line.Data.Length; i++)
            operand |= line.Data[i] << (8 * (i - 1));

        var mode = line.AccessMode;
        var result = new HoverOperand { Text = target.Operand! };

        if (mode == AccessMode.Relative)
        {
            var offset = (sbyte)line.Data[1];
            result.Value = new HoverNumber(line.Address + line.Data.Length + offset, true, true);
            result.Mode = $"a branch of {offset}";
        }
        else
        {
            result.Value = new HoverNumber(operand, line.Data.Length > 2, mode != AccessMode.Immediate);
            result.Mode = ModeName(mode);
        }

        Breakdown(result, target.Expression!, context);
        Effective(result, mode, operand, context.Runtime);

        return result;
    }

    private static HoverOperand DescribeExpression(string expression, Context context, string? text = null)
    {
        var result = new HoverOperand { Text = text ?? expression };

        try
        {
            var evaluator = context.State.CreateEvaluator();
            var (value, requiresReval) = evaluator.Evaluate(expression, new SourceFilePosition(), context.Procedure.Variables, context.Output?.Address ?? 0, true);

            if (requiresReval)
                result.Problem = "can't be worked out here.";
            else
                result.Value = HoverNumber.Of(value);
        }
        catch (Exception e)
        {
            result.Problem = $"can't be worked out: {e.Message}";
        }

        Breakdown(result, expression, context);
        return result;
    }

    // each name in the expression with its value, then the expression with the values put in
    private static void Breakdown(HoverOperand result, string expression, Context context)
    {
        if (expression.StartsWith('-') || expression.StartsWith('+'))
            return; // a relative label, which the value already says

        var names = Names().Matches(expression).Select(i => i.Groups["name"].Value).Distinct().ToList();
        if (names.Count == 0)
            return;

        var single = expression.Trim() == names[0];
        var values = new Dictionary<string, int>();

        foreach (var name in names)
        {
            if (!Lookup(name, context, out var variable, out var owner) || variable is DebuggerVariable || variable.RequiresReval)
                continue;

            values[name] = variable.Value;

            if (!single)
                result.Names.Add(new HoverName { Name = name, FullName = FullName(variable, owner), Type = TypeName(variable), Value = IsAddress(variable) ? HoverNumber.OfAddress(variable.Value) : HoverNumber.Of(variable.Value) });
        }

        if (values.Count == 0 || single)
            return;

        result.Substituted = Names().Replace(expression, m => values.TryGetValue(m.Groups["name"].Value, out var v) ? HoverNumber.Of(v).Hex : m.Value).Trim();
    }

    // where an indexed or indirect operand ends up, when the registers are known
    private static void Effective(HoverOperand result, AccessMode mode, int operand, IHoverRuntime? runtime)
    {
        if (runtime == null)
            return;

        int? address = mode switch
        {
            AccessMode.ZeroPageX => (operand + runtime.X) & 0xff,
            AccessMode.ZeroPageY => (operand + runtime.Y) & 0xff,
            AccessMode.AbsoluteX => (operand + runtime.X) & 0xffff,
            AccessMode.AbsoluteY => (operand + runtime.Y) & 0xffff,
            AccessMode.ZeroPage or AccessMode.Absolute => operand,
            AccessMode.ZeroPageIndirect => Pointer(runtime, operand & 0xff),
            AccessMode.IndirectX => Pointer(runtime, (operand + runtime.X) & 0xff),
            AccessMode.IndirectY => Pointer(runtime, operand & 0xff) is int p ? (p + runtime.Y) & 0xffff : null,
            _ => null
        };

        if (mode is AccessMode.ZeroPageX or AccessMode.AbsoluteX or AccessMode.IndirectX)
            result.Effective.Add(("X", HoverNumber.Of(runtime.X).ToString()));
        else if (mode is AccessMode.ZeroPageY or AccessMode.AbsoluteY or AccessMode.IndirectY)
            result.Effective.Add(("Y", HoverNumber.Of(runtime.Y).ToString()));

        if (mode is AccessMode.ZeroPageIndirect or AccessMode.IndirectX or AccessMode.IndirectY)
        {
            var pointer = mode == AccessMode.IndirectX ? (operand + runtime.X) & 0xff : operand & 0xff;
            var held = Pointer(runtime, pointer);
            result.Effective.Add(("pointer", held is int h ? $"{new HoverNumber(h, true).Hex} at {new HoverNumber(pointer, false).Hex}" : "can't be read"));
        }

        if (address is not int target)
            return;

        var addressText = new HoverNumber(target, target > 0xff).Hex;
        var note = runtime.AddressNote(target);
        result.Effective.Add(("address", note != null ? $"{addressText} ({note})" : addressText));

        if (runtime.Peek(target) is byte value)
            result.Effective.Add(("value", HoverNumber.Of(value).ToString()));
    }

    private static int? Pointer(IHoverRuntime runtime, int zp) =>
        runtime.Peek(zp) is byte lo && runtime.Peek((zp + 1) & 0xff) is byte hi ? lo | (hi << 8) : null;

    private static bool Lookup(string name, Context context, out IAsmVariable variable, out CompilerVariables? owner)
    {
        try
        {
            if (context.Procedure.Variables.TryGetValueIgnoringVisibility(name, new SourceFilePosition(), out var found, out var foundOwner) && found != null)
            {
                variable = found;
                owner = foundOwner;
                return true;
            }
        }
        catch (Exception)
        {
            // an ambiguous name, say it isn't known rather than fail the hover
        }

        variable = null!;
        owner = null;
        return false;
    }

    private static string FullName(IAsmVariable variable, CompilerVariables? owner) =>
        owner == null ? variable.Name : $"{owner.FullName}:{variable.Name}";

    private static bool IsAddress(IAsmVariable variable) => variable.VariableDataType != VariableDataType.Constant;

    public static string TypeName(IAsmVariable variable)
    {
        var type = variable.VariableDataType switch
        {
            VariableDataType.Constant => "constant",
            VariableDataType.ProcStart => "procedure",
            VariableDataType.ProcEnd => "end of a procedure",
            VariableDataType.SegmentStart => "segment",
            VariableDataType.LabelPointer => "label",
            VariableDataType.FixedStrings => "strings",
            var other => other.ToString().ToLowerInvariant().Replace("ptr", " ptr")
        };

        if (variable is AsmVariable { OperandLabel: true })
            type = $"operand label, {type}";

        if (variable.Array)
            type += variable.Length > 0 ? $"[{variable.Length}]" : "[]";

        return type;
    }

    private static string ModeName(AccessMode mode) => mode switch
    {
        AccessMode.Immediate => "immediate",
        AccessMode.ZeroPage => "zero page",
        AccessMode.ZeroPageX => "zero page,X",
        AccessMode.ZeroPageY => "zero page,Y",
        AccessMode.Absolute => "absolute",
        AccessMode.AbsoluteX => "absolute,X",
        AccessMode.AbsoluteY => "absolute,Y",
        AccessMode.Indirect => "indirect",
        AccessMode.IndirectX => "(zero page,X)",
        AccessMode.IndirectY => "(zero page),Y",
        AccessMode.IndAbsoluteX => "(absolute,X)",
        AccessMode.ZeroPageIndirect => "(zero page)",
        AccessMode.ZerpPageRel => "zero page, relative",
        var other => other.ToString().ToLowerInvariant()
    };
}
