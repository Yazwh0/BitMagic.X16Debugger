using BitMagic.X16Debugger.DebugableFiles;

namespace BitMagic.X16Debugger.Exceptions;

internal class DebugWrapperAlreadyLoadedException : Exception
{
    public DebugWrapper Wrapper { get; }

    public DebugWrapperAlreadyLoadedException(DebugWrapper wrapper) : base("Wrapper already loaded.")
    {
        Wrapper = wrapper;
    }
}
