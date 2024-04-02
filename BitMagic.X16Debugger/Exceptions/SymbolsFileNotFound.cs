namespace BitMagic.X16Debugger.Exceptions;

internal class SymbolsFileNotFound : Exception
{
    public string Filename { get; private set; }

    public SymbolsFileNotFound(string filename) : base($"Symbol file not found {filename}")
    {
        Filename = filename;
    }
}
