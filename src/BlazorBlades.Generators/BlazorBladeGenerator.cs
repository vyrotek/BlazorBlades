using BlazorBlades.Generators.Core;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorBlades.Generators
{
    [Generator]
    public class BlazorBladeGenerator : IIncrementalGenerator
    {
        private const string NonGenericBlazorBladeMetadataName = "BlazorBlades.BlazorBlade";
        private const string GenericBlazorBladeMetadataName = "BlazorBlades.BlazorBlade`1";
        private const string GlobalAliasPrefix = "global::";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var projectOptions = context.AnalyzerConfigOptionsProvider.Select(
                static (options, _) => GeneratorProjectOptions.Create(options.GlobalOptions)
            );

            var razorDocuments = context.AdditionalTextsProvider
                .Where(static file =>
                    file.Path.EndsWith(
                        GeneratorConstants.RazorFileExtension,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .Select(static (file, _) => file.Path);

            var compilationAndInputs = context.CompilationProvider.Combine(
                projectOptions.Combine(razorDocuments.Collect())
            );

            context.RegisterSourceOutput(
                compilationAndInputs,
                static (spc, source) =>
                {
                    var (compilation, (projectOptions, razorDocuments)) = source;

                    var blazorBladeSymbol = compilation.GetTypeByMetadataName(
                        NonGenericBlazorBladeMetadataName
                    );
                    var genericBlazorBladeSymbol = compilation.GetTypeByMetadataName(
                        GenericBlazorBladeMetadataName
                    );
                    if (blazorBladeSymbol is null || genericBlazorBladeSymbol is null)
                    {
                        return;
                    }

                    var razorProjectEngine = RazorComponentDiscovery.TryCreateProjectEngine(
                        projectOptions
                    );
                    if (razorProjectEngine is null)
                    {
                        return;
                    }

                    var generatedTypes = new HashSet<string>(StringComparer.Ordinal);

                    foreach (
                        var razorDocumentPath in razorDocuments.Distinct(
                            StringComparer.OrdinalIgnoreCase
                        )
                    )
                    {
                        var candidate = BlazorBladeGenerator.TryCreateCandidate(
                            compilation,
                            blazorBladeSymbol,
                            genericBlazorBladeSymbol,
                            razorProjectEngine,
                            projectOptions.ProjectDirectory,
                            razorDocumentPath
                        );

                        if (candidate is null || !generatedTypes.Add(candidate.ComponentTypeName))
                        {
                            continue;
                        }

                        spc.AddSource(
                            candidate.HintName,
                            SourceText.From(
                                BlazorBladeGenerator.CreateSource(candidate),
                                System.Text.Encoding.UTF8
                            )
                        );
                    }
                }
            );
        }

        private static RazorModelCandidate? TryCreateCandidate(
            Compilation compilation,
            INamedTypeSymbol blazorBladeSymbol,
            INamedTypeSymbol genericBlazorBladeSymbol,
            RazorProjectEngine razorProjectEngine,
            string? projectDirectory,
            string razorDocumentPath
        )
        {
            if (!RazorComponentDiscovery.TryGetGeneratedComponentSymbol(
                    compilation,
                    razorProjectEngine,
                    projectDirectory,
                    razorDocumentPath,
                    out var componentSymbol)
                || componentSymbol is null
                || !GeneratorTypeHelpers.IsTopLevelConcreteNonGenericType(componentSymbol))
            {
                return null;
            }

            if (!TryGetModelTypeName(
                    componentSymbol,
                    blazorBladeSymbol,
                    genericBlazorBladeSymbol,
                    out var modelTypeName))
            {
                return null;
            }

            var componentTypeName = componentSymbol.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );
            var ns = componentSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : componentSymbol.ContainingNamespace.ToDisplayString();

            return new RazorModelCandidate(
                ns,
                componentSymbol.Name,
                componentTypeName,
                modelTypeName,
                GetAccessibilityKeyword(componentSymbol.DeclaredAccessibility),
                GetHintName(componentTypeName)
            );
        }

        private static bool TryGetModelTypeName(
            INamedTypeSymbol componentSymbol,
            INamedTypeSymbol blazorBladeSymbol,
            INamedTypeSymbol genericBlazorBladeSymbol,
            out string? modelTypeName
        )
        {
            modelTypeName = null;

            for (var current = componentSymbol.BaseType; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, blazorBladeSymbol))
                {
                    return true;
                }

                if (!current.IsGenericType)
                {
                    continue;
                }

                if (
                    SymbolEqualityComparer.Default.Equals(
                        current.ConstructedFrom,
                        genericBlazorBladeSymbol
                    )
                )
                {
                    modelTypeName = GetModelTypeName(componentSymbol, current.TypeArguments[0]);
                    return true;
                }
            }

            return false;
        }

        private static string GetModelTypeName(
            INamedTypeSymbol componentSymbol,
            ITypeSymbol modelType
        )
        {
            if (
                modelType is INamedTypeSymbol namedType
                && SymbolEqualityComparer.Default.Equals(
                    namedType.ContainingType,
                    componentSymbol
                )
            )
            {
                return namedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            }

            return modelType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private static string GetAccessibilityKeyword(Accessibility accessibility) =>
            accessibility switch
            {
                Accessibility.Public => "public ",
                Accessibility.Internal => "internal ",
                _ => string.Empty,
            };

        private static string GetHintName(string metadataName) =>
            metadataName
                .Replace(GlobalAliasPrefix, string.Empty)
                .Replace('<', '[')
                .Replace('>', ']')
                .Replace('.', '_')
                .Replace(':', '_')
                + ".Blade"
                + GeneratorConstants.GeneratedCSharpFileExtension;

        private static string CreateSource(RazorModelCandidate candidate)
        {
            var namespaceDeclaration = candidate.Namespace is null
                ? string.Empty
                : $"namespace {candidate.Namespace};\n\n";

            return $$"""
                #nullable enable
                {{namespaceDeclaration}}{{candidate.Accessibility}}partial class {{candidate.TypeName}}
                {
                {{CreateBladeMethod(candidate)}}

                {{CreateRenderAsyncMethod(candidate)}}
                }
                """;
        }

        private static string CreateBladeMethod(RazorModelCandidate candidate) =>
            candidate.ModelTypeName is null
                ? $"    public static global::Microsoft.AspNetCore.Http.HttpResults.RazorComponentResult<{candidate.TypeName}> Blade(int statusCode = 200)\n        => Blade<{candidate.TypeName}>(statusCode);"
                : $"    public static global::Microsoft.AspNetCore.Http.HttpResults.RazorComponentResult<{candidate.TypeName}> Blade({candidate.ModelTypeName} model, int statusCode = 200)\n        => Blade<{candidate.TypeName}>(model, statusCode);";

        private static string CreateRenderAsyncMethod(RazorModelCandidate candidate) =>
            candidate.ModelTypeName is null
                ? $"    public static global::System.Threading.Tasks.Task<string> RenderAsync(global::System.IServiceProvider services)\n        => RenderAsync<{candidate.TypeName}>(services);"
                : $"    public static global::System.Threading.Tasks.Task<string> RenderAsync(global::System.IServiceProvider services, {candidate.ModelTypeName} model)\n        => RenderAsync<{candidate.TypeName}>(services, model);";

        private sealed class RazorModelCandidate
        {
            public RazorModelCandidate(
                string? ns,
                string typeName,
                string componentTypeName,
                string? modelTypeName,
                string accessibility,
                string hintName
            )
            {
                Namespace = ns;
                TypeName = typeName;
                ComponentTypeName = componentTypeName;
                ModelTypeName = modelTypeName;
                Accessibility = accessibility;
                HintName = hintName;
            }

            public string? Namespace { get; }

            public string TypeName { get; }

            public string ComponentTypeName { get; }

            public string? ModelTypeName { get; }

            public string Accessibility { get; }

            public string HintName { get; }
        }

    }
}
