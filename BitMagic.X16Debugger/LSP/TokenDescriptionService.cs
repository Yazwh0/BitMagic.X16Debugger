namespace BitMagic.X16Debugger.LSP;

internal class TokenDescriptionService(ITokenDescriptionProvider[] providers)
{
    public string? GetTokenDescription(string token)
    {
        if (token == null)
            return null;

        foreach (var provider in providers)
        {
            if (provider.Tokens.TryGetValue(token, out string value))
                return value;
        }
        return null;
    }
}

internal interface ITokenDescriptionProvider
{
    public Dictionary<string, string> Tokens { get; }
}

internal abstract class TokenDescriptionProvider : ITokenDescriptionProvider
{
    public Dictionary<string, string> Tokens { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public void Process(Dictionary<string, string> input)
    {
        Tokens.Clear();
        foreach (var kv in input)
        {
            Tokens.Add(kv.Key, kv.Value);
        }
    }
}