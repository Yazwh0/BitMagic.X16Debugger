using BitMagic.X16Debugger.Variables;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.VariablePresentationHint;

namespace BitMagic.X16Debugger.Tests.Variables;

[TestClass]
public class VariableIndexTests
{
    private static (string Value, ICollection<Variable> Variables) Children() =>
        ("2 items", new List<Variable> { new("0", "10", 0), new("1", "20", 0) });

    [TestMethod]
    public void NoSetter_IsMarkedReadOnly()
    {
        var index = new VariableIndex("Stack", Children);

        var attributes = index.GetVariable().PresentationHint!.Attributes.GetValueOrDefault();

        Assert.IsTrue(attributes.HasFlag(AttributesValue.ReadOnly));
    }

    [TestMethod]
    public void Setter_ClearsReadOnlyFlag()
    {
        var index = new VariableIndex("Stack", Children, (_, _) => { });

        var attributes = index.GetVariable().PresentationHint!.Attributes.GetValueOrDefault();

        Assert.IsFalse(attributes.HasFlag(AttributesValue.ReadOnly));
    }

    [TestMethod]
    public void SetVariable_WithSetter_ForwardsNameAndValue()
    {
        string? name = null;
        string? value = null;
        var index = new VariableIndex("Stack", Children, (n, v) => { name = n; value = v; });

        index.SetVariable(new SetVariableArguments { Name = "1", Value = "42" });

        Assert.AreEqual("1", name);
        Assert.AreEqual("42", value);
    }

    [TestMethod]
    public void SetVariable_WithoutSetter_DoesNotThrow()
    {
        var index = new VariableIndex("Stack", Children);

        index.SetVariable(new SetVariableArguments { Name = "0", Value = "42" });
    }
}
