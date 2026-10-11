using Microsoft.VisualStudio.TestTools.UnitTesting;
using BitMagic.X16Debugger.Hovers;

namespace BitMagic.X16Debugger.Tests.Hovers;

[TestClass]
public class HoverTextTests
{
    // '¦' marks the cursor, which is on the character after it
    private static HoverTarget? Find(string line)
    {
        var column = line.IndexOf('¦');
        Assert.AreNotEqual(-1, column, "no cursor in the test line");
        return HoverText.Find(line.Remove(column, 1), column);
    }

    [TestMethod]
    // instructions: the name, and the expression without its addressing mode
    [DataRow("    jsr ¦BSOUT", "BSOUT", "BSOUT", "BSOUT", "", "")]
    [DataRow("    jsr BSOU¦T", "BSOUT", "BSOUT", "BSOUT", "", "")]
    [DataRow("    jsr BSOUT¦", "BSOUT", "BSOUT", "BSOUT", "", "")]
    [DataRow("    lda #¦VALUE", "VALUE", "#VALUE", "VALUE", "#", "")]
    [DataRow("    lda ¦#VALUE", null, "#VALUE", "VALUE", "#", "")]
    [DataRow("    sta ¦lines,x", "lines", "lines,x", "lines", "", ",x")]
    [DataRow("    sta lines, ¦Y", null, "lines, Y", "lines", "", ",y")]
    [DataRow("    lda (¦ptr),y", "ptr", "(ptr),y", "ptr", "(", "),y")]
    [DataRow("    lda (¦ptr,x)", "ptr", "(ptr,x)", "ptr", "(", ",x)")]
    [DataRow("    jmp (¦vector)", "vector", "(vector)", "vector", "(", ")")]
    [DataRow("    lda <¦buffer", "buffer", "<buffer", "<buffer", "", "")]
    [DataRow("    adc #¦COUNT+1", "COUNT", "#COUNT+1", "COUNT+1", "#", "")]
    [DataRow("    adc #COUNT+¦1", null, "#COUNT+1", "COUNT+1", "#", "")]
    [DataRow("    lda ¦text:line_count", "text:line_count", "text:line_count", "text:line_count", "", "")]
    [DataRow("    lda text:line_¦count", "text:line_count", "text:line_count", "text:line_count", "", "")]
    [DataRow("    lda ¦:App:Main:x", "App:Main:x", ":App:Main:x", ":App:Main:x", "", "")]
    [DataRow("    lda #$¦ff", null, "#$ff", "$ff", "#", "")]
    [DataRow("    lda #%10¦10", null, "#%1010", "%1010", "#", "")]
    [DataRow("    lda #'¦A'", null, "#'A'", "'A'", "#", "")]
    [DataRow("    lda source: ¦$ffff, x", null, "$ffff, x", "$ffff", "", ",x")]
    [DataRow("    lda source: <lo: >hi: ¦table, x", "table", "table, x", "table", "", ",x")]
    [DataRow(".: lda ¦value", "value", "value", "value", "", "")]
    [DataRow("    lda ¦value ; a ; comment", "value", "value", "value", "", "")]
    [DataRow("    lda #';'¦+1", null, "#';'+1", "';'+1", "#", "")]
    public void Instruction(string line, string? name, string operand, string expression, string prefix, string suffix)
    {
        var target = Find(line);

        Assert.IsNotNull(target);
        Assert.AreEqual(HoverLineKind.Instruction, target.Kind);
        Assert.AreEqual(name, target.Name);
        Assert.AreEqual(operand, target.Operand);
        Assert.AreEqual(expression, target.Expression);
        Assert.AreEqual(prefix, target.Prefix);
        Assert.AreEqual(suffix, target.Suffix);
    }

    [TestMethod]
    [DataRow("    ¦lda #VALUE")]       // the mnemonic
    [DataRow("    rts¦ ")]
    [DataRow("    lda value ; ¦comment")]
    [DataRow("   ¦ ")]
    [DataRow("    .const ¦public X 1")]
    [DataRow("    .¦proc foo")]
    [DataRow("    .proc ¦foo")]         // not a directive with a value
    public void Nothing(string line)
    {
        var target = Find(line);

        Assert.IsTrue(target == null || target.Name == null && target.Operand == null, $"{target?.Name} {target?.Operand}");
    }

    [TestMethod]
    [DataRow(".¦loop:", "loop")]
    [DataRow("    .lo¦op:  ; a label", "loop")]
    public void Label(string line, string name)
    {
        var target = Find(line);

        Assert.IsNotNull(target);
        Assert.AreEqual(HoverLineKind.Label, target.Kind);
        Assert.AreEqual(name, target.Name);
        Assert.AreEqual(name, target.DefinedName);
    }

    [TestMethod]
    // the defined name, with the value it's given
    [DataRow(".const ¦width 40", "width", "width", "40")]
    [DataRow(".const private ¦width = 40", "width", "width", "40")]
    [DataRow(".const height=25, ¦cells=width*height", "cells", "cells", "width*height")]
    [DataRow(".const height=25, cells=width*¦height", "height", "cells", "width*height")]
    [DataRow(".const ¦height=25, cells=width*height", "height", "height", "25")]
    [DataRow(".export ¦feed_high feed:sample + 1", "feed_high", "feed_high", "feed:sample + 1")]
    [DataRow(".export feed_high feed:¦sample + 1", "feed:sample", "feed_high", "feed:sample + 1")]
    [DataRow(".export private feed_end = ¦feed:endproc", "feed:endproc", "feed_end", "feed:endproc")]
    [DataRow(".constvar byte ptr ¦vera_ctrl $9f25", "vera_ctrl", "vera_ctrl", "$9f25")]
    [DataRow(".constvar public ushort score ¦base + 2", "base", "score", "base + 2")]
    [DataRow(".var byte ¦lives 3", "lives", "lives", "3")]
    [DataRow(".padvar private byte ¦tmp", "tmp", "tmp", null)]
    public void Definition(string line, string name, string definedName, string? expression)
    {
        var target = Find(line);

        Assert.IsNotNull(target);
        Assert.AreEqual(HoverLineKind.Directive, target.Kind);
        Assert.AreEqual(name, target.Name);
        Assert.AreEqual(definedName, target.DefinedName);
        Assert.AreEqual(expression, target.Expression);
    }

    [TestMethod]
    [DataRow(".word $1234, ¦label, label + 2", "label", "label")]
    [DataRow(".word $1234, label, ¦label + 2", "label", "label + 2")]
    [DataRow(".byte $00, <¦table, >table", "table", "<table")]
    [DataRow(".byte ¦$00, $11", null, "$00")]
    public void Values(string line, string? name, string expression)
    {
        var target = Find(line);

        Assert.IsNotNull(target);
        Assert.AreEqual(HoverLineKind.Directive, target.Kind);
        Assert.AreEqual(name, target.Name);
        Assert.AreEqual(expression, target.Expression);
    }
}
