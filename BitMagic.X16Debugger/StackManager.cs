using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace BitMagic.X16Debugger
{
    internal class StackManager
    {
        private readonly Emulator _emulator;
        private bool _invalid = true;
        private string _value = "";
        private readonly ICollection<Variable> _data = new List<Variable>();

        public StackManager(Emulator emulator)
        {
            _emulator = emulator;
        }

        public (string Value, ICollection<Variable> Variables) GetStack()
        {
            if (_invalid)
                GenerateStack();

            return (_value, _data);
        }

        private void GenerateStack()
        {
            _value = $"{0x1ff - _emulator.StackPointer:##0} entries";
            _data.Clear();

            var stack = _emulator.Memory.Slice(_emulator.StackPointer + 1, 0x1ff - _emulator.StackPointer);

            for (var i = 0; i < stack.Length; i++)
                _data.Add(new Variable()
                {
                    Name = i.ToString(),
                    Type = "Byte",
                    PresentationHint = new VariablePresentationHint() { Kind = VariablePresentationHint.KindValue.Data },
                    Value = $"0x{stack[i]:X2}"
                });

            _invalid = false;
        }

        public void Invalidate()
        {
            _invalid = true;
        }
    }
}
