//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
//using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

//using static System.FormattableString;

//namespace BitMagic.X16Debugger;

//internal class SampleScope : SampleObject<Scope, object>, ISampleVariableContainer
//{
//    #region Private Fields

//    private bool expensive;
//    private List<SampleVariable> variables;

//    #endregion

//    #region Constructor

//    internal SampleScope(X16Debug adapter, string name, bool expensive)
//        : base(adapter)
//    {
//        this.variables = new List<SampleVariable>();

//        this.Name = name;
//        this.expensive = expensive;
//    }

//    #endregion

//    #region SampleObject Implementation

//    public override void Invalidate()
//    {
//        base.Invalidate();

//        foreach (SampleVariable variable in this.variables)
//        {
//            variable.Invalidate();
//        }
//    }

//    protected override bool IsSameFormat(object a, object b)
//    {
//        // Scopes don't have formatting.
//        return true;
//    }

//    protected override Scope CreateProtocolObject()
//    {
//        return new Scope(
//            name: this.Name,
//            variablesReference: this.Adapter.GetNextId(),
//            expensive: this.expensive);
//    }

//    #endregion

//    internal void AddVariable(SampleVariable variable)
//    {
//        this.variables.Add(variable);
//        variable.SetContainer(this);
//    }

//    #region ISampleVariableContainer Implementation

//    public string Name { get; private set; }

//    public IReadOnlyCollection<ISampleVariableContainer> ChildContainers
//    {
//        get { return this.variables; }
//    }

//    public IReadOnlyCollection<SampleVariable> Variables
//    {
//        get { return this.variables; }
//    }

//    public int VariableReference
//    {
//        get
//        {
//            return this.ProtocolObject?.VariablesReference ?? 0;
//        }
//    }

//    public VariablesResponse HandleVariablesRequest(VariablesArguments args)
//    {
//        return new VariablesResponse(variables: this.variables.Select(v => v.GetProtocolObject(args.Format)).ToList());
//    }

//    public SetVariableResponse HandleSetVariableRequest(SetVariableArguments arguments)
//    {
//        var variable = this.variables.FirstOrDefault(v => String.Equals(v.Name, arguments.Name, StringComparison.Ordinal));
//        if (variable == null)
//        {
//            throw new ProtocolException(Invariant($"Scope '{this.Name}' (varRef: {this.VariableReference}) does not contain a variable called '{arguments.Name}'!"));
//        }

//        variable.SetValue(arguments.Value);
//        variable.Invalidate();

//        return new SetVariableResponse(value: variable.GetValue(arguments.Format?.Hex ?? false));
//    }

//    public ISampleVariableContainer? Container
//    {
//        get { return null; }
//    }

//    #endregion
//}