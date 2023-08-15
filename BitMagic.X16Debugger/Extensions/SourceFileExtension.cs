using BitMagic.Common;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.Compiler.Extensions;

internal static class SourceFileExtension
{
    public static Source AsSource(this ISourceFile inp) => new Source()
    {
        Name = inp.Name,
        Path = inp.Path,
        SourceReference = inp.ReferenceId,
        Origin = inp.Origin.ToString(),
    };
}
