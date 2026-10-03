using BitMagic.Compiler;
using BitMagic.X16Debugger.DebugableFiles;
using Microsoft.CodeAnalysis;

namespace BitMagic.X16Debugger.Extensions;

internal static class CompileResultExtensions
{
    // todo: understand if the file has a header or not, assuming it does for now.
    internal static IList<BitMagicBinaryFile> CreateBinarySourceFiles(this CompileResult result)
    {
        var toReturn = new List<BitMagicBinaryFile>();

        // go through each output and create file on each
        foreach (var i in result.Data.Where(i => i.Value.Length != 0)) // dont process segment that do not have data
        {
            // the compiler decides which files get a load address header (eg the main program always does)
            var hasAddedHeader = i.Value.HasHeader;
            
            toReturn.Add(new BitMagicBinaryFile(result.Project.Code, i.Value, result, hasAddedHeader ? FileHeader.HeaderNotInCode : FileHeader.NoHeader));
        }

        return toReturn;
    }
}