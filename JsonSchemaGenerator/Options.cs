using CommandLine;

namespace JsonSchemaGenerator;

internal class Options
{
    [Option("output", Default = false, Required = true)]
    public string Output { get; set; } = "";
}
