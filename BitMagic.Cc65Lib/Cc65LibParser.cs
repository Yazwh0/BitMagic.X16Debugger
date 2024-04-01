using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace BitMagic.Cc65Lib;
public static class Cc65LibParser
{
    private const uint _objectHeader = 0x616E7A55;
    private const uint _libraryHeader = 0x7A55616E;

    public static Cc65Obj Parse(string filename, string sourcePath = "")
    {
        if (!File.Exists(filename))
            throw new FileNotFoundException(filename);

        using var fs = new FileStream(filename, FileMode.Open);
        using var reader = new BinaryReader(fs);

        var header = reader.ReadUInt32();
        Cc65Obj? toReturn = null;

        if (header == _libraryHeader)
        {
            var lib = ReadLibrary(reader);
            header = reader.ReadUInt32();
        }

        if (header != _objectHeader)
            throw new Exception($"File {filename} is not a recognised file.");

        toReturn = ReadObject(reader, filename, sourcePath);

        reader.Close();
        fs.Close();

        DisplayInfo(toReturn);

        return toReturn;
    }

    public static Cc65Library ParseLib(string filename)
    {
        if (!File.Exists(filename))
            throw new FileNotFoundException(filename);

        using var fs = new FileStream(filename, FileMode.Open);
        using var reader = new BinaryReader(fs);

        var header = reader.ReadUInt32();

        if (header != _libraryHeader)
        {
            throw new Exception($"File {filename} is not a library file.");
        }

        var lib = ReadLibrary(reader);

        reader.Close();
        fs.Close();

        return lib;
    }

    private static Cc65Library ReadLibrary(BinaryReader reader)
    {
        var toReturn = new Cc65Library();

        var version = reader.ReadUInt16();
        if (version != 0x000d)
            throw new Exception($"{version:X2} is a unsupported lib version");

        toReturn.Flags = reader.ReadUInt16();
        toReturn.IndexOfs = reader.ReadUInt32();

        // read the index
        reader.BaseStream.Position = toReturn.IndexOfs;
        var indexCount = reader.ReadCc65Var();

        for(var i = 0; i < indexCount; i++)
        {
            var indexEntry = new Cc65IndexEntry();

            indexEntry.Name = reader.ReadString();
            indexEntry.Flags = reader.ReadUInt16();
            indexEntry.Time = reader.ReadUInt32();
            indexEntry.Start = reader.ReadUInt32();
            indexEntry.Size = reader.ReadUInt32();

            toReturn.Index.Add(indexEntry.Name, indexEntry);
        }

        foreach (var i in toReturn.Index.Values)
        {
            reader.BaseStream.Position = i.Start;

            var header = reader.ReadUInt32();
            if (header != _objectHeader)
                throw new Exception($"Index Entry {i.Name} is not a recognised objdect.");

            i.Cc65Obj = ReadObject(reader, i.Name, "");
        }

        return toReturn;
    }

    private static Cc65Obj ReadObject(BinaryReader reader, string filename, string sourcePath)
    {
        if (!sourcePath.EndsWith(Path.DirectorySeparatorChar))
            sourcePath += Path.DirectorySeparatorChar;

        var toReturn = new Cc65Obj() { Offset = reader.BaseStream.Position - 4, Filename = filename, SourcePath = sourcePath };

        ReadHeader(reader, toReturn);

        if (toReturn.Version != 0x11)
            throw new Exception($"{toReturn.Version:X2} is a unsupported version.");

        ReadStringPool(reader, toReturn);
        ReadFiles(reader, toReturn);
        ReadLineInfos(reader, toReturn);
        ReadSegments(reader, toReturn);
        ReadExports(reader, toReturn);

        return toReturn;
    }

    public static void ReadExports(BinaryReader reader, Cc65Obj toReturn)
    {
        reader.BaseStream.Position = toReturn.Offset + toReturn.ExportOffs;

        uint count = reader.ReadCc65Var();

        for (var i = 0; i < count; i++)
        {
            var exportType = reader.ReadCc65Var();
            var addressSize = reader.ReadByte();

            var export = new Export() { ExportType = exportType, AddressSize = addressSize };
            toReturn.Exports.Add(export);

            var condesCount = exportType & Export.CondesMask;

            if (condesCount > 0)
                export.Data = reader.ReadBytes((int)condesCount);

            export.Name_StringId = reader.ReadCc65Var();

            if ((exportType & Export.Expression) != 0)
            {
                export.Expr = ReadExpression(reader);
            }
            else
            {
                export.Expr = new Expression() { ExpressionType = (byte)Expression.Literal, Value = reader.ReadInt32() };
            }

            if ((exportType & Export.Size) != 0)
            {
                export.ExportSize = reader.ReadCc65Var();
            }

            // read line info
            var lineInfoCount = reader.ReadCc65Var();

            for (var j = 0; j < lineInfoCount; j++)
            {
                export.DefinedLines.Add(reader.ReadCc65Var());
            }

            lineInfoCount = reader.ReadCc65Var();

            for (var j = 0; j < lineInfoCount; j++)
            {
                export.ReferencedLines.Add(reader.ReadCc65Var());
            }
        }
    }

    private static void ReadSegments(BinaryReader reader, Cc65Obj toReturn)
    {
        reader.BaseStream.Position = toReturn.Offset + toReturn.SegOffs;

        uint count = reader.ReadCc65Var();

        for (var i = 0; i < count; i++)
        {
            reader.ReadInt32(); // size is discarded
            uint nameId = reader.ReadCc65Var();
            reader.ReadCc65Var(); // flags are discarded
            uint size = reader.ReadCc65Var();
            uint allignment = reader.ReadCc65Var();
            byte segmentType = reader.ReadByte();
            uint fragmentCount = reader.ReadCc65Var();

            var name = toReturn.StringPool[(int)nameId];
            Console.WriteLine($"{segmentType:X2} - {name} -- {size:X4}");

            var segment = new Segment() { Name_stringId = nameId, Size = size, Allignment = allignment, SegmentType = segmentType };
            toReturn.Segments.Add(segment);

            for (var j = 0; j < fragmentCount; j++)
            {
                byte fragmentType = reader.ReadByte();
                var fragment = new Fragment() { FragmentType = fragmentType };

                segment.Fragments.Add(fragment);

                switch (fragmentType & Fragment.TypeMask)
                {
                    case Fragment.Literal:
                        fragment.Data = reader.ReadBytes((int)reader.ReadCc65Var());
                        break;
                    case Fragment.Expression:
                    case Fragment.SignedExpression:
                        fragment.Expr = ReadExpression(reader);
                        break;
                    case Fragment.Fill:
                        uint fragmentSize = reader.ReadCc65Var();
                        fragment.Data = new byte[fragmentSize];
                        break;
                    default:
                        throw new Exception($"Unklnown fragment type 0x{fragmentType:X02}");
                }

                var lineCount = reader.ReadCc65Var();
                for (var k = 0; k < lineCount; k++)
                {
                    fragment.LineInfo.Add(reader.ReadCc65Var());
                }
            }
        }
    }

    private static Expression ReadExpression(BinaryReader reader)
    {
        byte expressionType = reader.ReadByte();

        var toReturn = new Expression() { ExpressionType = expressionType };

        if (expressionType == Expression.ExprNull)
            return toReturn;

        if ((expressionType & Expression.TypeMask) == Expression.LeafNode)
        {
            switch ((uint)expressionType)
            {
                case Expression.Literal:
                    toReturn.Value = reader.ReadInt32();
                    break;
                case Expression.Symbol:
                    toReturn.ImportNumber = reader.ReadCc65Var();
                    break;
                case Expression.Section:
                    toReturn.SectionNumber = reader.ReadCc65Var();
                    break;
                default:
                    throw new Exception($"Unhandled expression type 0x{expressionType:X2}");
            }
        }
        else
        {
            toReturn.Left = ReadExpression(reader);
            toReturn.Right = ReadExpression(reader);
        }

        return toReturn;
    }

    private static void ReadStringPool(BinaryReader reader, Cc65Obj toReturn)
    {
        reader.BaseStream.Position = toReturn.Offset + toReturn.StrPoolOffs;

        uint count = reader.ReadCc65Var();

        for (var i = 0; i < count; i++)
        {
            toReturn.StringPool.Add(reader.ReadString());
        }
    }

    private static void ReadFiles(BinaryReader reader, Cc65Obj toReturn)
    {
        reader.BaseStream.Position = toReturn.Offset + toReturn.FileOffs;

        uint count = reader.ReadCc65Var();

        for (var i = 0; i < count; i++)
        {
            uint stringId = reader.ReadCc65Var();
            uint mtime = reader.ReadUInt32();
            uint size = reader.ReadCc65Var();

            toReturn.Files.Add(new FileInfo() { Filename_StringId = stringId, Time = mtime, Size = size });
        }
    }

    private static void ReadLineInfos(BinaryReader reader, Cc65Obj toReturn)
    {
        reader.BaseStream.Position = toReturn.Offset + toReturn.LineInfoOffs;

        uint count = reader.ReadCc65Var();

        for (var i = 0; i < count; i++)
        {
            uint line = reader.ReadCc65Var();
            uint col = reader.ReadCc65Var();
            uint stringId = reader.ReadCc65Var();
            uint type = reader.ReadCc65Var();

            var fileInfo = new LineInfo() { Line = line, Column = col, FileInfo_id = stringId, _Type = type };
            toReturn.Lines.Add(fileInfo);

            uint spanCount = reader.ReadCc65Var();

            for (var j = 0; j < spanCount; j++)
            {
                fileInfo.Spans.Add(reader.ReadCc65Var());
            }
        }
    }

    private static void ReadHeader(BinaryReader reader, Cc65Obj toReturn)
    {
        toReturn.Version = reader.ReadUInt16();
        toReturn.Flags = reader.ReadUInt16();
        toReturn.OptionOffs = reader.ReadUInt32();
        toReturn.OptionSize = reader.ReadUInt32();
        toReturn.FileOffs = reader.ReadUInt32();
        toReturn.FileSize = reader.ReadUInt32();
        toReturn.SegOffs = reader.ReadUInt32();
        toReturn.SegSize = reader.ReadUInt32();
        toReturn.ImportOffs = reader.ReadUInt32();
        toReturn.ImportSize = reader.ReadUInt32();
        toReturn.ExportOffs = reader.ReadUInt32();
        toReturn.ExportSize = reader.ReadUInt32();
        toReturn.DbgSymOffs = reader.ReadUInt32();
        toReturn.DbgSymSize = reader.ReadUInt32();
        toReturn.LineInfoOffs = reader.ReadUInt32();
        toReturn.LineInfoSize = reader.ReadUInt32();
        toReturn.StrPoolOffs = reader.ReadUInt32();
        toReturn.StrPoolSize = reader.ReadUInt32();
        toReturn.AssertOffs = reader.ReadUInt32();
        toReturn.AssertSize = reader.ReadUInt32();
        toReturn.ScopeOffs = reader.ReadUInt32();
        toReturn.ScopeSize = reader.ReadUInt32();
        toReturn.SpanOffs = reader.ReadUInt32();
        toReturn.SpanSize = reader.ReadUInt32();
    }

    private static void DisplayInfo(Cc65Obj l)
    {
        Console.WriteLine($"Version : {l.Version}");
        Console.WriteLine($"Flags : {l.Flags}");
        Console.WriteLine($"             Offset       Size");
        Console.WriteLine($"Options    0x{l.OptionOffs:X8}   0x{l.OptionSize:X8}");
        Console.WriteLine($"File       0x{l.FileOffs:X8}   0x{l.FileSize:X8}");
        Console.WriteLine($"Segment    0x{l.SegOffs:X8}   0x{l.SegSize:X8}");
        Console.WriteLine($"Import     0x{l.ImportOffs:X8}   0x{l.ImportSize:X8}");
        Console.WriteLine($"Export     0x{l.ExportOffs:X8}   0x{l.ExportSize:X8}");
        Console.WriteLine($"Dbg Symb   0x{l.DbgSymOffs:X8}   0x{l.DbgSymSize:X8}");
        Console.WriteLine($"Line Info  0x{l.LineInfoOffs:X8}   0x{l.LineInfoSize:X8}");
        Console.WriteLine($"Strings    0x{l.StrPoolOffs:X8}   0x{l.StrPoolSize:X8}");
        Console.WriteLine($"Assert     0x{l.AssertOffs:X8}   0x{l.AssertSize:X8}");
        Console.WriteLine($"Scope      0x{l.ScopeOffs:X8}   0x{l.ScopeSize:X8}");
        Console.WriteLine($"Span       0x{l.SpanOffs:X8}   0x{l.SpanSize:X8}");

        Console.WriteLine();
        Console.WriteLine("Files :");
        foreach (var i in l.Files)
        {
            Console.WriteLine($"Name: {(l.StringPool[(int)i.Filename_StringId])}, Modified {i.Time}, Size {i.Size}");
        }

        Console.WriteLine();
        Console.WriteLine("Exports : ");
        foreach (var i in l.Exports)
        {
            Console.WriteLine($"{l.StringPool[(int)i.Name_StringId]} : {i.GetValue(l)}");
        }

        Console.WriteLine();
        Console.WriteLine("Segments : ");
        foreach (var i in l.Segments)
        {
            Console.WriteLine($"Segment {l.StringPool[(int)i.Name_stringId]}");

        }

        Console.WriteLine();
        Console.WriteLine("Files : ");
        foreach (var i in l.Files)
        {
            Console.WriteLine(l.StringPool[(int)i.Filename_StringId]);
        }

        return;

        Console.WriteLine();
        Console.WriteLine("Strings : ");
        foreach (var i in l.StringPool)
        {
            Console.WriteLine(i);
        }

        Console.WriteLine();
        foreach (var i in l.Segments)
        {
            Console.WriteLine($"Segment {l.StringPool[(int)i.Name_stringId]}");
            foreach (var j in i.Fragments)
            {
                Console.Write(j.Getvalue(l, false));
                switch (j.FragmentType & Fragment.TypeMask)
                {
                    case Fragment.Literal:
                        if (j.FragmentType == Fragment.Literal)
                        {
                            var linfo = l.Lines[(int)j.LineInfo.First()];
                            var file = l.Files[(int)linfo.FileInfo_id];
                            Console.Write($" ; {l.StringPool[(int)file.Filename_StringId]} : {linfo.Line} = ");
                        }
                        break;
                }
                Console.WriteLine();
            }
        }
    }
}

public static class BinaryReaderExtensions
{
    public static uint ReadCc65Var(this BinaryReader reader)
    {
        byte running;

        uint value = 0;
        int shift = 0;
        do
        {
            running = reader.ReadByte();

            value |= ((uint)(running & 0x7f)) << shift;

            shift += 7;
        } while ((running & 0x80) != 0);

        return value;
    }

    public static string ReadCc65String(this BinaryReader reader)
    {
        var length = reader.ReadCc65Var();

        var toReturn = new string(reader.ReadChars((int)length));

        return toReturn;
    }
}

public class Cc65Library
{
    public ushort Flags { get; set; }
    public uint IndexOfs { get; set; }
    public Dictionary<string, Cc65IndexEntry> Index { get; } = new();
    
}

public class Cc65IndexEntry
{
    public string Name { get; set; } = "";
    public ushort Flags { get; set; }
    public UInt32 Time { get; set; }
    public UInt32 Start { get; set; }
    public UInt32 Size { get; set; }
    public Cc65Obj? Cc65Obj { get; set; } = null;
}

public class Cc65Obj
{
    public string Filename { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public long Offset { get; set; }

    public ushort Version;        /* 16: Version number */
    public ushort Flags;          /* 16: flags */
    public uint OptionOffs;     /* 32: Offset to option table */
    public uint OptionSize;     /* 32: Size of options */
    public uint FileOffs;       /* 32: Offset to file table */
    public uint FileSize;       /* 32: Size of files */
    public uint SegOffs;        /* 32: Offset to segment table */
    public uint SegSize;        /* 32: Size of segment table */
    public uint ImportOffs;     /* 32: Offset to import list */
    public uint ImportSize;     /* 32: Size of import list */
    public uint ExportOffs;     /* 32: Offset to export list */
    public uint ExportSize;     /* 32: Size of export list */
    public uint DbgSymOffs;     /* 32: Offset to list of debug symbols */
    public uint DbgSymSize;     /* 32: Size of debug symbols */
    public uint LineInfoOffs;   /* 32: Offset to list of line infos */
    public uint LineInfoSize;   /* 32: Size of line infos */
    public uint StrPoolOffs;    /* 32: Offset to string pool */
    public uint StrPoolSize;    /* 32: Size of string pool */
    public uint AssertOffs;     /* 32: Offset to assertion table */
    public uint AssertSize;     /* 32: Size of assertion table */
    public uint ScopeOffs;      /* 32: Offset into scope table */
    public uint ScopeSize;      /* 32: Size of scope table */
    public uint SpanOffs;       /* 32: Offset into span table */
    public uint SpanSize;       /* 32: Size of span table */

    public List<string> StringPool { get; } = new();
    public List<FileInfo> Files { get; } = new();
    public List<LineInfo> Lines { get; } = new();
    public List<Segment> Segments { get; } = new();
    public List<Export> Exports { get; } = new();

    //public IEnumerable<string> GenerateCodeStrings(string cc65Segment)
    //{
    //    var p = "";
    //    if (!string.IsNullOrWhiteSpace(SourcePath))
    //        p = Path.GetFullPath(SourcePath);

    //    yield return $".scope {ScopeName}";

    //    yield return $".{cc65Segment}:";

    //    var segment = Segments.FirstOrDefault(i => GetLibraryString(i.Name_stringId) == cc65Segment);

    //    if (segment == null)
    //    {
    //        yield return ".endscope";
    //        yield break;
    //    }

    //    var hasPath = !string.IsNullOrWhiteSpace(p);

    //    foreach (var i in segment.Fragments)
    //    {
    //        var lineInfo = Lines[(int)i.LineInfo[0]];
    //        var file = Files[(int)lineInfo.FileInfo_id];
    //        var filename = hasPath ? Path.GetFullPath(GetLibraryString(file.Filename_StringId), p) : Path.GetFullPath(p);
    //        if (!string.IsNullOrEmpty(filename))
    //        {
    //            yield return $".map {filename} {lineInfo.Line - 1}"; // -1 as bitmagic is 0 based
    //            yield return i.Getvalue(this, true);
    //            continue;
    //        }
    //        yield return i.Getvalue(this, false);
    //    }

    //    yield return ".endscope";
    //}

    //public IEnumerable<string> GenerateExportsStrings()
    //{
    //    yield return $".scope {ScopeName}";

    //    foreach (var i in Exports)
    //    {
    //        yield return $".constvar proc {GetLibraryString(i.Name_StringId)} {i.GetValue(this)}";
    //    }

    //    yield return ".endscope";
    //}

    private string GetLibraryString(uint stringId) => StringPool[(int)stringId];
}

public class FileInfo
{
    public uint Filename_StringId { get; set; }
    public uint Time { get; set; }
    public uint Size { get; set; }
}

public class LineInfo
{
    public uint Line { get; set; }
    public uint Column { get; set; }
    public uint FileInfo_id { get; set; }
    public uint _Type { get; set; }
    public List<uint> Spans { get; } = new();
}

public class Segment
{
    public uint Name_stringId { get; set; }
    public uint Size { get; set; }
    public uint Allignment { get; set; }
    public byte SegmentType { get; set; }
    public List<Fragment> Fragments { get; } = new();
}

public class Fragment
{
    public const uint ByteMask = 0x07;
    public const uint TypeMask = 0x38;
    public const uint Literal = 0x00;
    public const uint Expression = 0x08;
    public const uint Expression_8bit = 0x08 + 0x01;
    public const uint Expression_16bit = 0x08 + 0x02;
    public const uint Expression_24bit = 0x08 + 0x03;
    public const uint Expression_32bit = 0x08 + 0x04;
    public const uint SignedExpression = 0x10;
    public const uint SignedExpression_8Bit = 0x10 + 0x01;
    public const uint SignedExpression_16Bit = 0x10 + 0x02;
    public const uint SignedExpression_24Bit = 0x10 + 0x03;
    public const uint SignedExpression_32Bit = 0x10 + 0x04;

    public const uint Fill = 0x20;

    public byte FragmentType { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public Expression? Expr { get; set; }
    public List<uint> LineInfo { get; } = new();

    public int Size()
    {
        var type = ((uint)FragmentType & TypeMask);

        switch (type)
        {
            case Expression:
            case SignedExpression:
                if (Expr == null)
                    throw new Exception("Expression is null when fragment is a expression type");

                return ((uint)FragmentType & ByteMask) switch
                {
                    1 => 1,
                    2 => 2,
                    3 => 3,
                    4 => 4,
                    _ => throw new Exception($"Unknown fragment type 0x{FragmentType}")
                };
            case Literal:
            case Fill:
                return Data.Length;
        }

        throw new Exception($"Unhandled type 0x{type:X2}");
    }

    public bool IsExpression => ((uint)FragmentType & TypeMask) switch
    {
        Expression => true,
        SignedExpression => true,
        _ => false
    };

    public string Getvalue(Cc65Obj lib, bool hasMap)
    {
        var type = ((uint)FragmentType & TypeMask);

        switch (type)
        {
            case Expression:
            case SignedExpression:
                if (Expr == null)
                    throw new Exception("Expression is null when fragment is a expression type");

                return ((uint)FragmentType & ByteMask) switch
                {
                    1 => ".byte ",
                    2 => ".word ",
                    3 => ".?? ",
                    4 => ".dword ",
                    _ => throw new Exception($"Unknown fragment type 0x{FragmentType}")
                } + Expr.GetValue(lib);
            case Literal:
                return (hasMap ? ".code" : ".byte ") + string.Join(", ", Data.Select(i => $"${i:X2}"));
            case Fill:
                return $".pad {Data.Length}";
        }

        throw new Exception($"Unhandled type 0x{type:X2}");
    }
}

public class Expression
{
    public const uint ExprNull = 0x00;
    public const uint TypeMask = 0xc0;

    public const uint BinaryNode = 0x00;
    public const uint UnaryNode = 0x40;
    public const uint LeafNode = 0x80;

    public const uint Literal = LeafNode + 0x01;
    public const uint Symbol = LeafNode + 0x02;
    public const uint Section = LeafNode + 0x03;
    public const uint Segment = LeafNode + 0x04;
    public const uint MemoryArea = LeafNode + 0x05;
    public const uint Ulabel = LeafNode + 0x06;

    public const uint Plus = BinaryNode + 0x01;
    public const uint Minus = BinaryNode + 0x02;
    public const uint Multiply = BinaryNode + 0x03;
    public const uint Divide = BinaryNode + 0x04;
    public const uint Mod = BinaryNode + 0x05;
    public const uint Or = BinaryNode + 0x06;
    public const uint Xor = BinaryNode + 0x07;
    public const uint And = BinaryNode + 0x08;
    public const uint Shl = BinaryNode + 0x09;
    public const uint Shr = BinaryNode + 0x0a;
    public const uint Equal = BinaryNode + 0x0b;
    public const uint NotEqual = BinaryNode + 0x0c;
    public const uint LessThan = BinaryNode + 0x0d;
    public const uint GreaterThan = BinaryNode + 0x0e;
    public const uint LessThanEqual = BinaryNode + 0x0f;
    public const uint GreaterThanEqual = BinaryNode + 0x10;
    public const uint BoolAnd = BinaryNode + 0x11;
    public const uint BoolOr = BinaryNode + 0x12;
    public const uint BoolXor = BinaryNode + 0x13;
    public const uint Max = BinaryNode + 0x14;
    public const uint Min = BinaryNode + 0x15;

    public const uint Unary_Minus = UnaryNode + 0x01;
    public const uint Not = UnaryNode + 0x02;
    public const uint Swap = UnaryNode + 0x03;
    public const uint BoolNot = UnaryNode + 0x04;
    public const uint Bank = UnaryNode + 0x05;

    public const uint Byte0 = UnaryNode + 0x08;
    public const uint Byte1 = UnaryNode + 0x09;
    public const uint Byte2 = UnaryNode + 0x0a;
    public const uint Byte3 = UnaryNode + 0x0b;
    public const uint Word0 = UnaryNode + 0x0c;
    public const uint Word1 = UnaryNode + 0x0d;
    public const uint FarAddr = UnaryNode + 0x0e;
    public const uint Dword = UnaryNode + 0x0f;
    public const uint NearAddr = UnaryNode + 0x10;

    public byte ExpressionType { get; set; }
    public Expression? Left { get; set; } = null;
    public Expression? Right { get; set; } = null;

    public int Value { get; set; }
    public uint ImportNumber { get; set; }
    public uint SectionNumber { get; set; }

    public string GetValue(Cc65Obj lib)
    {
        switch ((uint)ExpressionType)
        {
            case Literal:
                return Value.ToString();
            case Section:
                return lib.StringPool[(int)lib.Segments[(int)SectionNumber].Name_stringId];
            case Symbol:
                var x = lib.Exports[(int)ImportNumber];
                throw new Exception("Symbols not supported");
        }

        return (uint)ExpressionType switch
        {
            Plus => $"({Left.GetValue(lib)} + {Right.GetValue(lib)})",
            Minus => $"({Left.GetValue(lib)} - {Right.GetValue(lib)})",
            Multiply => $"({Left.GetValue(lib)} * {Right.GetValue(lib)})",
            Divide => $"({Left.GetValue(lib)} / {Right.GetValue(lib)})",
            Mod => $"({Left.GetValue(lib)} % {Right.GetValue(lib)})",
            Or => $"({Left.GetValue(lib)} | {Right.GetValue(lib)})",
            Xor => $"({Left.GetValue(lib)} ^ {Right.GetValue(lib)})",
            And => $"({Left.GetValue(lib)} & {Right.GetValue(lib)})",
            Shl => $"({Left.GetValue(lib)} << {Right.GetValue(lib)})",
            Shr => $"({Left.GetValue(lib)} >> {Right.GetValue(lib)})",
            Equal => $"({Left.GetValue(lib)} == {Right.GetValue(lib)})",
            NotEqual => $"({Left.GetValue(lib)} != {Right.GetValue(lib)})",
            GreaterThan => $"({Left.GetValue(lib)} > {Right.GetValue(lib)})",
            LessThan => $"({Left.GetValue(lib)} < {Right.GetValue(lib)})",
            GreaterThanEqual => $"({Left.GetValue(lib)} >= {Right.GetValue(lib)})",
            LessThanEqual => $"({Left.GetValue(lib)} <= {Right.GetValue(lib)})",
            BoolAnd => $"({Left.GetValue(lib)} && {Right.GetValue(lib)})",
            BoolOr => $"({Left.GetValue(lib)} || {Right.GetValue(lib)})",
            BoolXor => $"({Left.GetValue(lib)} ^^ {Right.GetValue(lib)})",
            Max => $"Math.Max({Left.GetValue(lib)}, {Right.GetValue(lib)})",
            Min => $"Math.Min({Left.GetValue(lib)}, {Right.GetValue(lib)})",
            Unary_Minus => $"-{Left.GetValue(lib)}",
            Not => $"~{Left.GetValue(lib)}",
            Swap => throw new Exception("Unsupported SWAP expression"),
            BoolNot => $"!{Left.GetValue(lib)}",
            Bank => throw new Exception("Unsupported BANK expression"),
            Byte0 => $"<{Left.GetValue(lib)}",
            Byte1 => $">{Left.GetValue(lib)}",
            Byte2 => $"^{Left.GetValue(lib)}",
            Byte3 => $"(({Left.GetValue(lib)} && 0xff000000) >> 24)",
            Word0 => $"({Left.GetValue(lib)} & 0xffff)",
            Word1 => $"(({Left.GetValue(lib)} & 0xffff) >> 16)",
            Dword => $"{Left.GetValue(lib)}",
            NearAddr => Left.GetValue(lib),
            FarAddr => Left.GetValue(lib),
            _ => throw new Exception($"Unsupported expression type 0x{ExpressionType:X2}")
        };
    }
}

public class Export
{
    public const uint CondesMask = 0x0007;
    public const uint Sizeless = 0x0000;
    public const uint Size = 0x0008;

    public const uint Constant = 0x0000;
    public const uint Expression = 0x0010;

    public const uint Label = 0x0020;
    public const uint CheapLocal = 0x0040;
    public const uint SymExport = 0x0080;
    public const uint SymImport = 0x0100;

    public uint Name_StringId { get; set; }
    public uint ExportType { get; set; }
    public byte AddressSize { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public Expression? Expr { get; set; } = null;
    public uint ExportSize { get; set; }
    public List<uint> DefinedLines { get; set; } = new();
    public List<uint> ReferencedLines { get; set; } = new();

    public string GetValue(Cc65Obj lib)
    {
        return Expr.GetValue(lib);
    }
}