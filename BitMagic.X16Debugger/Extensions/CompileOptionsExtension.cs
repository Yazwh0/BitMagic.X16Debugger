using BitMagic.TemplateEngine.Compiler;
using BitMagic.Compiler;

namespace BitMagic.X16Debugger.Extensions;

internal static class CompileOptionsExtension
{
    public static TemplateOptions AsTemplateOptions(this CompileOptions options, string basePath) => new TemplateOptions
    {
        BinFolder = Path.GetFullPath(Path.Join(basePath, options?.BinFolder ?? "Bin")),
        Rebuild = options?.Rebuild ?? false,
        SaveGeneratedTemplate = options?.SaveGeneratedTemplate ?? false,
        SavePreGeneratedTemplate = options?.SavePreGeneratedTemplate ?? false,
        BasePath = basePath
    };
}