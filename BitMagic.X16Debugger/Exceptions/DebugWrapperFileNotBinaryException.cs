using BitMagic.Common;

namespace BitMagic.X16Debugger.Exceptions;

public class DebugWrapperFileNotBinaryException : Exception
{
    public ISourceFile File { get; }
    public DebugWrapperFileNotBinaryException(ISourceFile file) : base("File is not IBinaryFile")
    {
        File = file;
    }
}