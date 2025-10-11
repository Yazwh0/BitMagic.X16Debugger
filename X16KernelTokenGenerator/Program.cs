using System.Text;
using System.Text.Json;

Console.WriteLine("Generating Tokens");

static async Task<string> DownloadFileAsync(string url)
{
    HttpClient client = new();

    var response = await client.GetAsync(url);
    response.EnsureSuccessStatusCode();

    return await response.Content.ReadAsStringAsync();
}

static string EscapeForCSharp(string input)
{
    return input
        .Replace("\\", "\\\\")   // Escape backslashes
        .Replace("\"", "\\\"")   // Escape double quotes
        .Replace("\n", "\\n")    // Escape newlines
        .Replace("\r", "\\r")    // Escape carriage returns
        .Replace("\t", "\\t")   // Escape tabs
        //.Replace("{", "\\{")    // Escape backspaces
        //.Replace("}", "\\}")    // Escape backspaces
        ;
}

var toProcess = await DownloadFileAsync(@"https://raw.githubusercontent.com/X16Community/x16-docs/refs/heads/master/X16%20Reference%20-%2005%20-%20KERNAL.md");

var result = new Dictionary<string, string>();
var thisFunction = new StringBuilder();

var currentFunction = "";
const string functionNameHeader = "#### Function Name: ";
foreach (var line in toProcess.Split("\n"))
{
    if (line.StartsWith(functionNameHeader))
    {
        thisFunction.Clear();
        currentFunction = line.Substring(functionNameHeader.Length).Replace("'", "").Replace("`", "").Replace("\"", "").Trim();

        thisFunction.AppendLine($"**{currentFunction}**");
    }
    else if (line.StartsWith("---"))
    {
        if (currentFunction != "")
        {
            result[currentFunction] = thisFunction.ToString();
        }
        currentFunction = "";
    }
    else if (currentFunction != "")
    {
        thisFunction.AppendLine(line);
    }
}

var toWrite = @"using System.Text.Json;

namespace BitMagic.X16Debugger.LSP;

internal class X16KernelDocumentation : TokenDescriptionProvider
{
    public X16KernelDocumentation()
    {
        Process(JsonSerializer.Deserialize<Dictionary<string, string>>(""" +
EscapeForCSharp(JsonSerializer.Serialize(result)) + @"""));
    }
}";

await File.WriteAllTextAsync("C:\\Documents\\Source\\BitMagic\\BitMagic.X16Debugger\\BitMagic.X16Debugger\\LSP\\X16KernelDocumentation.cs", toWrite);

Console.WriteLine("Done");