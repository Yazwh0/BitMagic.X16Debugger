using BitMagic.X16Debugger;
using CommandLine;
using JsonSchemaGenerator;
using Newtonsoft.Json.Schema.Generation;
using Newtonsoft.Json.Serialization;

Console.WriteLine("BitMagic - Debug Project Schema Generator");

ParserResult<Options>? argumentsResult;
try
{
    argumentsResult = Parser.Default.ParseArguments<Options>(args);
}
catch (Exception ex)
{
    Console.WriteLine("Error processing arguments:");
    Console.WriteLine(ex.Message);
    return;
}

var options = argumentsResult?.Value ?? throw new Exception();

var generator = new JSchemaGenerator();
generator.ContractResolver = new CamelCasePropertyNamesContractResolver();
generator.SchemaIdGenerationHandling = SchemaIdGenerationHandling.TypeName;
generator.SchemaReferenceHandling = SchemaReferenceHandling.All;
generator.SchemaLocationHandling = SchemaLocationHandling.Inline;
generator.DefaultRequired = Newtonsoft.Json.Required.AllowNull;

var schema = generator.Generate(typeof(X16DebugProject), Newtonsoft.Json.Required.AllowNull, null);

var filename = Path.GetFullPath(options.Output);

Console.WriteLine($"Writing to : {filename}");
File.WriteAllText(filename, schema.ToString());
Console.WriteLine("Done.");
