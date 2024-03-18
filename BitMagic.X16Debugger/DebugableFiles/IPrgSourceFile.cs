using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using System.Collections.Generic;

namespace BitMagic.X16Debugger.DebugableFiles;

// source code that generated a file
internal interface IPrgSourceFile
{
    IEnumerable<IPrgFile> Output { get; }
    string Filename { get; }
    public Dictionary<string, IEnumerable<Breakpoint>> Breakpoints { get; }
    public IEnumerable<string> ReferencedFilenames { get; }
}
