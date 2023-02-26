using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BitMagic.X16Debugger
{
    internal class SpriteManager
    {
        private readonly Emulator _emulator;
        private readonly VariableChildren[] _children = new VariableChildren[128];
        private readonly Variable[] _variables = new Variable[128];

        public SpriteManager(Emulator emulator)
        {
            _emulator= emulator;
            var sprites = _emulator.Sprites;

            for(var i = 0; i < sprites.Length; i++)
            {
                var index = i;
                _children[i] = new VariableChildren($"Sprite {index}", "Sprite",
                    () => _emulator.Sprites[index].Depth == 0 ? "Disabled" : "Enabled",
                    new IVariableMap[]
                    {
                        new VariableMap("X", "int", () => $"{_emulator.Sprites[index].X}"),
                        new VariableMap("Y", "int", () => $"{_emulator.Sprites[index].Y}"),
                        new VariableMap("Width", "int", () => $"{_emulator.Sprites[index].Width}"),
                        new VariableMap("Height", "int", () => $"{_emulator.Sprites[index].Height}"),
                        new VariableMap("Address", "Word", () => $"0x{_emulator.Sprites[index].Address:X5}"),
                        new VariableMap("Palette Offset", "Byte", () => $"{_emulator.Sprites[index].PaletteOffset}"),
                        new VariableMap("Bpp", "int", () => GetBpp(_emulator.Sprites[index].Mode)),
                        new VariableMap("Depth", "int", () => $"{_emulator.Sprites[index].Depth}"),
                        new VariableMap("H Flip", "bool", () => $"{(_emulator.Sprites[index].Mode & 0x01) != 0}"),
                        new VariableMap("V Flip", "bool", () => $"{(_emulator.Sprites[index].Mode & 0x02) != 0}"),
                        new VariableMap("Collision Mask", "byte", () => $"0b{Convert.ToString(_emulator.Sprites[index].CollisionMask, 2).PadLeft(4, '0')}"),
                    });

                _variables[i] = _children[i].GetVariable();
            }
        }

        private static string GetBpp(uint mode) => (mode & 0b1000000) == 0 ? "4" : "8";

        public void Register(VariableManager variableManager)
        {
            foreach(var child in _children)
            {
                variableManager.Register(child);
            }
        }

        public (string Value, ICollection<Variable> Data) GetFunction()
        {
            string Value;
            var sprites = _emulator.Sprites;

            var cnt = 0;
            for (var i = 0; i < 128; i++)
                cnt += sprites[i].Depth != 0 ? 1 : 0;

            if (cnt == 0)
                Value = "None active";
            else
                Value =$"{cnt:0} active";

            foreach (var i in _children)
                i.GetVariable();        // updates the objects

            return (Value, _variables);
        }
    }
}
