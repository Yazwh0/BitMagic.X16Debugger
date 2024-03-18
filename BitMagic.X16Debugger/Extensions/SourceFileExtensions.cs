using BitMagic.Common;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.Extensions;

public static class SourceFileExtensions
{
    public static Source AsSource(this ISourceFile source) => new Source
    {
        Name = Path.GetFileName(source.Name),
        Path = source.Path,
        SourceReference = source.ReferenceId,
        Origin = source.Origin.ToString()
    };
}