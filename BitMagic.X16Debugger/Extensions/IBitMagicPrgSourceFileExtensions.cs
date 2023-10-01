using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.Common;

namespace BitMagic.X16Debugger.Extensions;

internal static class IBitMagicPrgSourceFileExtensions
{
    public static IEnumerable<string> GetFixedSourceFilenames(this IBitMagicPrgSourceFile source)
    {
        yield return source.Filename.FixFilename();

        foreach (var i in source.ReferencedFilenames)
            yield return i.FixFilename();
    }
}