using BitMagic.Compiler;
using BitMagic.Compiler.CodingSeb;
using BitMagic.X16Debugger.Variables;
using BitMagic.X16Emulator;
using CodingSeb.ExpressionEvaluator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger;

internal class ExpressionManager
{
    private readonly Asm6502ExpressionEvaluator _evaluator;
    private readonly VariableManager _variableManager;
    private CompileState? _state = null;
    private readonly Emulator _emulator;
    private readonly MemoryWrapper _memoryWrapper;

    internal ExpressionManager(VariableManager variableManager, Emulator emulator)
    {
        _evaluator = new Asm6502ExpressionEvaluator();
        _evaluator.StringifyFunction = Stringify;
        _variableManager = variableManager;
        _emulator = emulator;
        _memoryWrapper = new MemoryWrapper(() => _emulator.Memory.ToArray());

        _evaluator.EvaluateVariable += _evaluator_EvaluateVariable;
    }

    public void SetState(CompileState state)
    {
        _state = state;
    }

    internal static string Stringify(object obj)
    {
        if (obj == null)
            return "";

        if (obj is ushort)
            return "0x" + ((ushort)obj).ToString("X4");

        if (obj is byte)
            return "0x" + ((byte)obj).ToString("X2");

        if (obj is uint)
            return "0x" + ((uint)obj).ToString("X8");

        return obj.ToString();
    }

    private void _evaluator_EvaluateVariable(object? sender, VariableEvaluationEventArg e)
    {
        var tree = _variableManager.ObjectTree;

        if (tree.ContainsKey(e.Name))
        {
            e.Value = tree[e.Name];
            return;
        }

        if (_state == null)
            return;

        if (_state.Evaluator.Variables != null &&
            _state.Evaluator.Variables.TryGetValue(e.Name, new Common.SourceFilePosition(), out var result))
        {
            e.Value = result.GetActualValue(_memoryWrapper);
            return;
        }

        var asmValue = _state.Evaluator.Evaluate(e.Name, new Common.SourceFilePosition(), _state.Procedure.Variables, 0, false);

        if (!asmValue.RequiresRecalc)
            e.Value = asmValue.Result;
    }

    /// <summary>
    /// For reuse, rather than returning data via DAP
    /// </summary>
    public object? EvaluateExpression(string expression, EventHandler<FunctionEvaluationEventArg>? functionCallback)
    {
        object? result = null;

        if (functionCallback != null)
            _evaluator.EvaluateFunction += functionCallback;

        result = _evaluator.Evaluate(expression);

        if (functionCallback != null)
            _evaluator.EvaluateFunction -= functionCallback;

        return result;
    }

    public EvaluateResponse Evaluate(EvaluateArguments arguments)
    {
        object? result = null;
        var toReturn = new EvaluateResponse();

        try
        {
            result = _evaluator.Evaluate(arguments.Expression);
            result = Stringify(result);
        }
        catch (Exception e)
        {
            toReturn.Result = e.Message;
        }

        if (result != null)
            toReturn.Result = result.ToString();

        return toReturn;
    }

    public string Evaluate(string expression)
    {
        var result = _evaluator.Evaluate(expression);
        return Stringify(result);
    }

    public string FormatMessage(string input)
    {
        var test = $"$\"{input}\"";

        try
        {
            var r = _evaluator.Evaluate(test);

            return Stringify(r);
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    public bool ConditionMet(string input)
    {
        try
        {
            var r = _evaluator.Evaluate(input);

            return r.Truthy();
        }
        catch (Exception e)
        {
            return false;
        }
    }

}

public static class ObjectTruthy
{
    public static bool Truthy(this object r)
    {
        try
        {
            if (r == null)
                return false;

            if (r is byte r_byte)
                return r_byte != 0;

            if (r is sbyte r_sbyte)
                return r_sbyte != 0;

            if (r is short r_short)
                return r_short != 0;

            if (r is ushort r_ushort)
                return r_ushort != 0;

            if (r is int r_int)
                return r_int != 0;

            if (r is uint r_uint)
                return r_uint != 0;

            if (r is long u_long)
                return u_long != 0;

            if (r is ulong u_ulong)
                return u_ulong != 0;

            if (r is float f_float)
                return f_float != 0;

            if (r is double d_double)
                return d_double != 0;

            if (r is decimal d_decimal)
                return d_decimal != 0;

            if (r is Array d_array)
                return d_array.Length != 0;

            if (r is bool r_b)
                return r_b;

            if (r is string r_s)
                return !string.IsNullOrWhiteSpace(r_s);

            return true; // its an object that has a value..?
        }
        catch (Exception e)
        {
            return false;
        }
    }
}