using BigMagic.TemplateEngine.Compiler;
using BitMagic.Common;
using BitMagic.Compiler;
using BitMagic.TemplateEngine.X16;
using DiscUtils.Fat;

namespace BitMagic.X16Debugger.DebugableFiles;

internal class BitmagicBuilder
{
    private readonly DebugableFileManager _fileManager;
    private readonly CodeGeneratorManager _codeGeneratorManager;
    private readonly IEmulatorLogger _logger;

    public BitmagicBuilder(DebugableFileManager fileManager, CodeGeneratorManager codeGeneratorManager, IEmulatorLogger logger)
    {
        _logger = logger;
        _fileManager = fileManager;
        _codeGeneratorManager = codeGeneratorManager;
    }

    public IList<BitMagicPrgFile> Build(X16DebugProject debugProject)
    {
        var project = new Project();
        _logger.Log($"Compiling {debugProject.Source} ");

        project.Code = new ProjectTextFile(debugProject.Source);
        project.Code.Generate();

        var engine = CsasmEngine.CreateEngine();
        var content = project.Code.GetContent();

        if (!string.IsNullOrWhiteSpace(content))
        {
            var templateResult = engine.ProcessFile(content, "main.dll").GetAwaiter().GetResult();

            templateResult.ReferenceId = _codeGeneratorManager.Register(debugProject.Source, templateResult);
            var filename = Path.GetFileNameWithoutExtension(debugProject.Source) + ".generated.bmasm";
            templateResult.Name = filename;
            templateResult.Path = filename;

            templateResult.Parent = project.Code;
            project.Code = templateResult;
        }

        var compiler = new Compiler.Compiler(project);

        var compileResult = compiler.Compile().GetAwaiter().GetResult();

        if (compileResult.Warnings.Any())
        {
            _logger.LogLine("Warnings:");
            foreach (var warning in compileResult.Warnings)
            {
                _logger.LogLine(warning);
            }
        }
        else
        {
            _logger.LogLine("Done.");
        }

        var toReturn = new List<BitMagicPrgFile>();
        foreach(var bitmagicPrg in BitMagicPrgFile.ProcessCompileResult(compileResult))
        {
            _fileManager.Addfile(bitmagicPrg);
            toReturn.Add(bitmagicPrg);
        }

        return toReturn;
    }
}
