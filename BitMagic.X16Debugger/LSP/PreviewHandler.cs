using MediatR;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Server;

namespace BitMagic.X16Debugger.LSP;

[Method("bitmagic/preview", Direction.ClientToServer)]
internal class PreviewHandler(ServiceManager serviceManager) : IJsonRpcRequestHandler<PreviewHandler.PreviewParameters, PreviewHandler.PreviewResult>, IDoesNotParticipateInRegistration
{
    public async Task<PreviewHandler.PreviewResult> Handle(PreviewHandler.PreviewParameters request, CancellationToken cancellationToken)
    {
        var m = serviceManager.DebugableFileManager;

        // should check path?
        if (Path.GetDirectoryName(request.Filename) != "\\generated")
            return new PreviewResult() { Content = [] };

        var generatedName = Path.GetFileName(request.Filename);

        var f = m.GetFile_New(generatedName);

        if (f == null)
            return new PreviewResult() { Content = [] };

        Console.WriteLine($"Serving virtual doc: {generatedName}");
        return new PreviewResult() { Content = f.Content };
    }

    public class PreviewParameters : IRequest<PreviewResult>
    {
        public string Filename { get; set; } = "";
    }

    public class PreviewResult
    {
        public IReadOnlyList<string> Content { get; set; } = [];
    }
}
