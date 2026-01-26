using System.Linq.Expressions;
using System.Text;

namespace BitMagic.X16Debugger.Variables;

public class MemoryWrapper
{
    internal readonly Func<byte[]> _values;
    public MemoryWrapper(Func<byte[]> values)
    {
        _values = values;
    }

    public MemoryLocation this[int index]
    {
        get
        {
            return new MemoryLocation(_values, index);
        }
    }

    public class MemoryLocation
    {
        internal readonly Func<byte[]> _values;
        internal int _index;
        internal MemoryLocation(Func<byte[]> values, int index)
        {
            _values = values;
            _index = index;
        }

        public byte Byte => _values()[_index];
        public sbyte Sbyte => (sbyte)_values()[_index];
        public char Char => (char)_values()[_index];
        public short Short => BitConverter.ToInt16(_values(), _index);
        public ushort Ushort => BitConverter.ToUInt16(_values(), _index);
        public int Int => BitConverter.ToInt32(_values(), _index);
        public uint Uint => BitConverter.ToUInt32(_values(), _index);
        public long Long => BitConverter.ToInt64(_values(), _index);
        public ulong Ulong => BitConverter.ToUInt64(_values(), _index);
        public string String
        {
            get
            {
                var sb = new StringBuilder();

                var values = _values();

                for (var i = _index; i < values.Length && values[i] != 0 && i < _index + 1024; i++)
                    sb.Append((char)values[i]);

                return sb.ToString();
            }
        }
        public string FixedString(int length)
        {
            var sb = new StringBuilder();

            var values = _values();

            for (var i = _index; i < values.Length && i < length + _index; i++)
                sb.Append((char)values[i]);

            return sb.ToString();
        }

        public override string ToString() => _values()[_index].ToString();
    }
}
