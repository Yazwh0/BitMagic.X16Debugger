using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BitMagic.X16Debugger;

internal class VariablesManager
{

}

internal interface IVariableMap
{
    string Name { get; }
    Variable GetVariable();
}

internal class VariableMap : IVariableMap
{
    private readonly Variable _variable;
    public string Name => _variable.Name;

    public Func<string> GetValue { get; }

    public VariableMap(string name, string type, Func<string> getFunction)
    {
        _variable = new Variable()
        {
            Name = name,
            Type = type,
        };

        GetValue = getFunction;
    }

    public Variable GetVariable()
    {
        _variable.Value = GetValue();
        return _variable;
    }
}

internal class VariableMemory : IVariableMap
{
    private readonly Variable _variable;
    public string Name => _variable.Name;
    public Func<string> GetValue { get; }

    internal VariableMemory(string name, string memoryReference, Func<string> getFunction)
    {
        _variable = new Variable()
        {
            Name = name,
            Type = "Byte[]",
            MemoryReference = memoryReference,
        };

        GetValue = getFunction;
    }

    public Variable GetVariable()
    {
        _variable.Value = GetValue();
        return _variable;
    }
}
