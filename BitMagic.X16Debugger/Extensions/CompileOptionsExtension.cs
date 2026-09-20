using BitMagic.TemplateEngine.Compiler;
using BitMagic.Compiler;

namespace BitMagic.X16Debugger.Extensions;

internal static class CompileOptionsExtension
{
    public static TemplateOptions AsTemplateOptions(this CompileOptions options, string basePath) => new TemplateOptions
    {
        // Path.Combine (not Path.Join): an absolute options.BinFolder must replace basePath
        // rather than be appended to it - Join always concatenates regardless of rootedness.
        BinFolder = Path.GetFullPath(Path.Combine(basePath, options?.BinFolder ?? "bin")),
        Rebuild = options?.Rebuild ?? false,
        SaveGeneratedTemplate = options?.SaveGeneratedTemplate ?? false,
        SavePreGeneratedTemplate = options?.SavePreGeneratedTemplate ?? false,
        BasePath = basePath
    };
}