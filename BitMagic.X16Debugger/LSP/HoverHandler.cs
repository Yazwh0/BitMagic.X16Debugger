using BitMagic.Common;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace BitMagic.X16Debugger.LSP;

internal class HoverHandler(DocumentCache documentCache, TokenDescriptionService tokenDescriptionService) : IHoverHandler
{
    public HoverRegistrationOptions GetRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("bmasm"),
            WorkDoneProgress = false
        };
    }

    public async Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        var file = documentCache.GetFile(request.TextDocument.Uri.GetFileSystemPath().FixFilename());

        if (file.Length == 0) // file not found
            return null;

        var word = GetWordAtCharIndex(file[request.Position.Line], request.Position.Character);

        if (string.IsNullOrEmpty(word))
            return null;

        var tokenDescription = tokenDescriptionService.GetTokenDescription(word);

        if (tokenDescription is not null)
        {
            return new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(
                new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = tokenDescription
                }),
                Range = new Range(request.Position, request.Position)
            };
        }

        return null;
    }

    private static string? GetWordAtCharIndex(string input, int index)
    {
        if (string.IsNullOrWhiteSpace(input) || index < 0 || index >= input.Length)
            return null;

        // Expand left to find the start of the word
        int start = index;
        while (start > 0 && !char.IsWhiteSpace(input[start - 1]))
            start--;

        // Expand right to find the end of the word
        int end = index;
        while (end < input.Length && !char.IsWhiteSpace(input[end]))
            end++;

        return input.Substring(start, end - start);
    }
}
