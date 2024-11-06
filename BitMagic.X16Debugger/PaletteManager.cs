using BitMagic.Common;
using BitMagic.X16Debugger.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger;

internal class PaletteManager
{
    private readonly Emulator _emulator;
    private readonly VariableMap[] _children = new VariableMap[256];
    private readonly Variable[] _variables  = new Variable[256];

    public PaletteManager(Emulator emulator)
    {
        _emulator = emulator;

        var palette = _emulator.Palette;

        for(var i = 0; i < palette.Length; i++)
        {
            var index = i;

            _children[i] = new VariableMap($"Colour 0x{index:X2}", "", () => $"#{_emulator.Palette[index].ToVariableColour()}", () => _emulator.Palette[index].ToExpression());
            _variables[i] = _children[i].GetVariable();
        }
    }

    public (string Value, ICollection<Variable> Data) GetFunction()
    {
        foreach (var i in _children)
            i.GetVariable();        // updates the objects

        return ("", _variables);
    }
}

internal static class PaletteExtensionMethods
{
    public static string ToVariableColour(this PixelRgba pixel) => $"{pixel.R & 0xf:X1}{pixel.G & 0xf:X1}{pixel.B & 0xf:X1}";
    public static int ToExpression(this PixelRgba pixel) => pixel.R << 16 + pixel.G << 8 + pixel.B; // should this be the same as memory??
}