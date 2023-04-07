using BitMagic.Common;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

            _children[i] = new VariableMap($"Colour 0x{index:X2}", "string", () => $"#{_emulator.Palette[index].ToVariableColour()}", () => _emulator.Palette[index].ToExpression());
            _variables[i] = _children[i].GetVariable();
        }
    }

    //public void Register(VariableManager variableManager)
    //{
    //    foreach (var child in _children)
    //    {
    //        variableManager.Register(child.v);
    //    }
    //}

    public (string Value, ICollection<Variable> Data) GetFunction()
    {
        foreach (var i in _children)
            i.GetVariable();        // updates the objects

        return ("", _variables);
    }
}

internal static class PalletteExtensionMethods
{
    public static string ToVariableColour(this PixelRgba pixel) => $"{pixel.R & 0xf:X1}{pixel.G & 0xf:X1}{pixel.B & 0xf:X1}";
    public static int ToExpression(this PixelRgba pixel) => pixel.R << 16 + pixel.G << 8 + pixel.B; // should this be the same as memory??
}