using BitMagic.X16Debugger.Variables;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.VariablePresentationHint;

namespace BitMagic.X16Debugger.Tests.Variables;

[TestClass]
public class VariableMapTests
{
    [TestMethod]
    public void NoSetter_IsMarkedReadOnly()
    {
        var map = new VariableMap("A", "byte", () => (byte)5);

        var variable = map.GetVariable();

        Assert.IsNull(map.SetValue);
        Assert.IsTrue(variable.PresentationHint!.Attributes.GetValueOrDefault().HasFlag(AttributesValue.ReadOnly));
    }

    [TestMethod]
    public void Setter_ClearsReadOnlyFlag()
    {
        var map = new VariableMap("A", "byte", () => (byte)5, setValue: _ => { });

        var variable = map.GetVariable();

        Assert.IsNotNull(map.SetValue);
        Assert.IsFalse(variable.PresentationHint!.Attributes.GetValueOrDefault().HasFlag(AttributesValue.ReadOnly));
    }

    [TestMethod]
    public void SetVariable_WithSetter_InvokesItWithTheNewValue()
    {
        string? written = null;
        var map = new VariableMap("A", "byte", () => (byte)5, setValue: v => written = v);

        map.SetVariable(new SetVariableArguments { Name = "A", Value = "42" });

        Assert.AreEqual("42", written);
    }

    [TestMethod]
    public void SetVariable_WithoutSetter_DoesNotThrow()
    {
        var map = new VariableMap("A", "byte", () => (byte)5);

        map.SetVariable(new SetVariableArguments { Name = "A", Value = "42" });
    }

    [TestMethod]
    public void ExplicitAttribute_IsPreservedAlongsideAutoReadOnly()
    {
        var map = new VariableMap("Negative", "bool", () => true, attribute: AttributesValue.IsBoolean);

        var attributes = map.GetVariable().PresentationHint!.Attributes.GetValueOrDefault();

        Assert.IsTrue(attributes.HasFlag(AttributesValue.IsBoolean));
        Assert.IsTrue(attributes.HasFlag(AttributesValue.ReadOnly));
    }

    [TestMethod]
    public void ExplicitAttribute_WithSetter_IsPreservedWithoutReadOnly()
    {
        var map = new VariableMap("Negative", "bool", () => true, attribute: AttributesValue.IsBoolean, setValue: _ => { });

        var attributes = map.GetVariable().PresentationHint!.Attributes.GetValueOrDefault();

        Assert.IsTrue(attributes.HasFlag(AttributesValue.IsBoolean));
        Assert.IsFalse(attributes.HasFlag(AttributesValue.ReadOnly));
    }
}
