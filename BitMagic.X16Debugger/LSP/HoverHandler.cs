using BitMagic.Common;
using BitMagic.X16Debugger.Hovers;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace BitMagic.X16Debugger.LSP;

internal class HoverHandler(DocumentCache documentCache, TokenDescriptionService tokenDescriptionService, ServiceManager serviceManager) : IHoverHandler
{
    public HoverRegistrationOptions GetRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("bmasm"),
            WorkDoneProgress = false
        };
    }

    public Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        var path = request.TextDocument.Uri.GetFileSystemPath().FixFilename();
        var file = documentCache.GetFile(path);

        if (file.Length == 0 || request.Position.Line >= file.Length) // file not found
            return Task.FromResult<Hover?>(null);

        var lineText = file[request.Position.Line];
        var column = request.Position.Character;

        // what the last good build says about it
        var built = serviceManager.HoverSnapshot?.Hover(path, request.Position.Line, lineText, column);

        // and, for a KERNAL routine, its documentation
        var target = HoverText.Find(lineText, column);
        var kernal = target?.Name != null ? tokenDescriptionService.GetTokenDescription(target.Name) : null;

        if (built == null && kernal == null)
            return Task.FromResult<Hover?>(null);

        var text = string.Join("\n\n---\n\n", new[] { built?.Result.ToMarkdown(), kernal }.Where(i => !string.IsNullOrEmpty(i)));

        var (start, end) = built != null ? (built.Value.Start, built.Value.End) : (target!.NameStart, target.NameEnd);

        return Task.FromResult<Hover?>(new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = text
            }),
            Range = new Range(request.Position.Line, start, request.Position.Line, end)
        });
    }
}
