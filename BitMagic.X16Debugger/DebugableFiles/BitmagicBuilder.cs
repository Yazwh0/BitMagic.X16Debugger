using BitMagic.TemplateEngine.Compiler;
using BitMagic.Common;
using BitMagic.Compiler;
using BitMagic.Compiler.Exceptions;
using BitMagic.TemplateEngine.X16;
using BitMagic.Compiler.Files;
using BitMagic.X16Debugger.Extensions;

namespace BitMagic.X16Debugger.DebugableFiles;

internal class BitmagicBuilder
{
    private readonly DebugableFileManager _fileManager;
    private readonly CodeGeneratorManager _codeGeneratorManager;
    private readonly DebugActionManager _debugActionManager;
    private readonly IEmulatorLogger _logger;

    public BitmagicBuilder(DebugableFileManager fileManager, CodeGeneratorManager codeGeneratorManager,
            DebugActionManager debugActionManager, IEmulatorLogger logger)
    {
        _logger = logger;
        _fileManager = fileManager;
        _codeGeneratorManager = codeGeneratorManager;
        _debugActionManager = debugActionManager;
    }

    /// <summary>
    /// Build project and return the main binary file
    /// </summary>
    /// <param name="debugProject"></param>
    /// <param name="outputFilename">File the main segment is written to, eg 'GAME.PRG' or 'BIN/GAME.PRG'. If blank it is
    /// the source name with a .prg extension.</param>
    /// <returns>Binary file for the main segment</returns>
    public async Task<(DebugWrapper?, CompileState)> Build(string source, string basePath, CompileOptions? compileOptions, string? outputFilename = null)
    {
        try
        {
            return await BuildCore(source, basePath, compileOptions, outputFilename);
        }
        catch (Exception e) when (e is not CompilerException and not TemplateException)
        {
            // anything not already a build error would otherwise surface as an unhelpful crash.
            throw new CompilerGeneralException($"Internal error building '{source}': {e.Message}", e);
        }
    }

    private async Task<(DebugWrapper?, CompileState)> BuildCore(string source, string basePath, CompileOptions? compileOptions, string? outputFilename)
    {
        var project = new Project();
        _logger.LogLine($"Compiling {source} ");

        if (compileOptions != null)
            project.CompileOptions = compileOptions;

        // the compiler names the main segment's file after this, and marks it as the main output. '/' as it's an
        // SD card path, which works the same on Linux and Windows.
        if (!string.IsNullOrWhiteSpace(outputFilename))
            project.OutputFile.Filename = outputFilename.Trim().Replace('\\', '/');

        source = Path.GetFullPath(Path.Combine(basePath, source)).FixFilename();
        var codeFile = new BitMagicProjectFile(source);
        project.Code = codeFile;
        try
        {
            // not a File.Exists check, the document cache can hold unsaved editor content.
            await codeFile.Load();
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new CompilerFileNotFound(source);
        }

        var engine = CsasmEngine.CreateEngine();
        var content = project.Code.Content;

        if (content.Any()) // ??
        {
            var templateOptions = project.CompileOptions!.AsTemplateOptions(basePath);
            var templateResult = await engine.ProcessFile(project.Code, source, templateOptions, _logger); ;

            templateResult.ReferenceId = _codeGeneratorManager.Register(source, templateResult);
            var filename = (Path.GetFileNameWithoutExtension(source) + ".generated.bmasm"); //.FixFilename();

            templateResult.SetName(filename);
            templateResult.SetParentAndMap(project.Code);

            if (project.CompileOptions != null && project.CompileOptions.SaveGeneratedBmasm)
            {
                var generatedPath = Path.Combine(templateOptions.BinFolder, filename);
                try
                {
                    File.WriteAllText(generatedPath, templateResult.Source.Code);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw new CompilerGeneralException($"Cannot write generated file '{generatedPath}': {e.Message}", e);
                }
            }

            project.Code = templateResult;
        }

        var compiler = new Compiler.Compiler(project, _debugActionManager, _logger);

        var compileResult = await compiler.Compile();

        compileResult.CreateBinarySourceFiles();
        project.Code.MapChildren();

        _fileManager.ClearFiles(project.Code); // clear everything out ready to replace it.
        _fileManager.AddFiles(project.Code); // loads whole family and sets their ID

        var mainFile = compileResult.Data.Values.FirstOrDefault(i => i.IsMain);

        DebugWrapper? toReturn = null;
        if (mainFile != null)
        {
            toReturn = _fileManager.GetFile_New(mainFile.FileName.ToUpper());
        }

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
            _logger.LogLine("... Done.");
        }

        return (toReturn, compileResult.State);
    }
}
