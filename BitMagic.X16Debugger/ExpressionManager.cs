using CodingSeb.ExpressionEvaluator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using System.Text.RegularExpressions;


namespace BitMagic.X16Debugger;

internal class ExpressionManager
{
    private readonly ExpressionEvaluator _evaluator;
    private readonly IdManager _idManager;
    private readonly VariableManager _variableManager;
    private readonly Regex _expressionExtractor = new Regex("\\{(.*?)\\}");

    internal ExpressionManager(IdManager idManager, VariableManager variableManager)
    {
        _evaluator = new ExpressionEvaluator(idManager);
        _idManager = idManager;
        _variableManager = variableManager;

        _evaluator.EvaluateVariable += _evaluator_EvaluateVariable;
    }

    private void _evaluator_EvaluateVariable(object? sender, VariableEvaluationEventArg e)
    {
        var tree = _variableManager.ObjectTree;

        if (!tree.ContainsKey(e.Name))
            return;

        e.Value = tree[e.Name];
    }

    public EvaluateResponse Evaluate(EvaluateArguments arguments)
    {
        object? result = null;
        var toReturn = new EvaluateResponse();

        try
        {
            result = _evaluator.Evaluate(arguments.Expression);
        }
        catch (Exception e)
        {
            toReturn.Result = e.Message;
        }

        if (result != null)
            toReturn.Result = result.ToString();

        return toReturn;
    }

    public string FormatMessage(string input)
    {
        var test = $"$\"{input}\"";

        try
        {
            var r = _evaluator.Evaluate(test);
            if (r == null)
                return "";

            return r.ToString() ?? "";
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