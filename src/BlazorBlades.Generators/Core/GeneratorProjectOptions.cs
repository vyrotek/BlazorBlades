using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorBlades.Generators.Core
{
    internal sealed class GeneratorProjectOptions
    {
        public GeneratorProjectOptions(string? projectDirectory, string? rootNamespace)
        {
            ProjectDirectory = projectDirectory;
            RootNamespace = rootNamespace;
        }

        public string? ProjectDirectory { get; }

        public string? RootNamespace { get; }

        public static GeneratorProjectOptions Create(AnalyzerConfigOptions options) => new(
            GetGlobalOption(options, GeneratorConstants.ProjectDirectoryBuildProperty),
            GetGlobalOption(options, GeneratorConstants.RootNamespaceBuildProperty)
                ?? GetGlobalOption(options, GeneratorConstants.AssemblyNameBuildProperty)
        );

        private static string? GetGlobalOption(AnalyzerConfigOptions options, string key) =>
            options.TryGetValue(key, out var value) ? value : null;
    }
}
