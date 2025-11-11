
using OmniSharp.Extensions.LanguageServer.Server;

namespace BitMagic.X16Debugger.LSP;

internal class ClientNotificationService
{
    private LanguageServer? _languageServer;

    internal void SetLanguageServer(LanguageServer languageServer)
    {
        _languageServer = languageServer;
    }

    public void SendNotfication<T>(string notification, T parameters)
    {
        if (_languageServer != null)
            _languageServer.SendNotification(notification, parameters);
    }
}
