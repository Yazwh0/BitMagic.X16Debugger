using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.VariablePresentationHint;

namespace BitMagic.X16Debugger;

internal class VariableManager
{
    private readonly Dictionary<int, IVariableMap> _variables = new();
    private readonly IdManager _idManager;

    public VariableManager(IdManager idManager)
    {
        _idManager = idManager;
    }

    public IVariableMap? Get(int id)
    {
        if (_variables.ContainsKey(id))
            return _variables[id];

        return null;
    }

    public VariableChildren? GetChildren(int id)
    {
        if (_variables.ContainsKey(id))
            return _variables[id] as VariableChildren;

        return null;
    }

    public VariableIndex? GetIndex(int id)
    {
        if (_variables.ContainsKey(id))
            return _variables[id] as VariableIndex;

        return null;
    }

    public VariableChildren Register(VariableChildren variable)
    {
        variable.Id = _idManager.GetId();
        _variables.Add(variable.Id, variable);
        return variable;
    }

    public VariableIndex Register(VariableIndex variable)
    {
        variable.Id = _idManager.GetId();
        _variables.Add(variable.Id, variable);
        return variable;
    }
}

internal interface IVariableMap
{
    int Id { get; }
    string Name { get; }
    Variable GetVariable();
}

internal class VariableMap : IVariableMap
{
    private readonly Variable _variable;
    public string Name => _variable.Name;
    public int Id => 0;

    public Func<string> GetValue { get; }

    public VariableMap(string name, string type, Func<string> getFunction,
        VariablePresentationHint.KindValue kindValue = VariablePresentationHint.KindValue.Property,
        VariablePresentationHint.AttributesValue attribute = AttributesValue.None
        )
    {
        _variable = new Variable()
        {
            Name = name,
            Type = type,
            PresentationHint = new VariablePresentationHint() { Kind = kindValue, Attributes = attribute }
        };

        GetValue = getFunction;
    }

    public Variable GetVariable()
    {
        _variable.Value = GetValue();
        return _variable;
    }
}

internal class VariableChildren : IVariableMap
{
    public readonly ICollection<IVariableMap> Children;
    private readonly Variable _variable;
    public string Name => _variable.Name;
    public int Id { get => _variable.VariablesReference; set => _variable.VariablesReference = value; }

    public Func<string> GetValue { get; }

    public VariableChildren(string name, string type, Func<string> getFunction, ICollection<IVariableMap> children,
        VariablePresentationHint.KindValue kindValue = VariablePresentationHint.KindValue.Property,
        VariablePresentationHint.AttributesValue attribute = AttributesValue.None
        )
    {
        _variable = new Variable()
        {
            Name = name,
            Type = type,
            PresentationHint = new VariablePresentationHint() { Kind = kindValue, Attributes = attribute }
        };

        GetValue = getFunction;
        Children = children;
    }

    public Variable GetVariable()
    {
        _variable.Value = GetValue();
        _variable.NamedVariables = Children.Count;
        return _variable;
    }
}

internal class VariableIndex : IVariableMap
{
    private readonly Variable _variable;
    public string Name => _variable.Name;
    public int Id { get => _variable.VariablesReference; set => _variable.VariablesReference = value; }
    public Func<(string Value, ICollection<Variable> Variables)> GetValues { get; }

    internal VariableIndex(string name, Func<(string Name, ICollection<Variable> Variables)> getFunction)
    {
        _variable = new Variable()
        {
            Name = name,
            Type = "Byte[]",
            PresentationHint = new VariablePresentationHint() { Kind = VariablePresentationHint.KindValue.Data }
        };
        GetValues = getFunction;
    }

    public Variable GetVariable()
    {
        (string Value, ICollection<Variable> Variables) = GetValues();
        _variable.Value = Value;
        _variable.IndexedVariables = Variables.Count;
        return _variable;
    }

    public IEnumerable<Variable> GetChildren()
    {
        return GetValues().Variables;
    }
}

internal class VariableMemory : IVariableMap
{
    private readonly Variable _variable;
    public string Name => _variable.Name;
    public int Id => 0;
    public Func<string> GetValue { get; }

    internal VariableMemory(string name, string memoryReference, Func<string> getFunction)
    {
        _variable = new Variable()
        {
            Name = name,
            Type = "Byte[]",
            MemoryReference = memoryReference,
            PresentationHint = new VariablePresentationHint() { Kind = VariablePresentationHint.KindValue.Data }
        };

        GetValue = getFunction;
    }

    public Variable GetVariable()
    {
        _variable.Value = GetValue();
        return _variable;
    }
}
