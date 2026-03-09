using BlazorBlades.Generators.Core;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlazorBlades.Generators
{
    [Generator]
    public class MapEndpointsGenerator : IIncrementalGenerator
    {
        private const string MapEndpointsInterfaceName = "IMapEndpoints";
        private const string MapEndpointsInterfaceMetadataName = "BlazorBlades.IMapEndpoints";
        private const string MapEndpointsSourceHintName = "BlazorBlades_MapEndpoints.g.cs";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var candidateClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) =>
                        node is ClassDeclarationSyntax classSyntax
                        && GeneratorTypeHelpers.CouldBeInterfaceCandidate(
                            classSyntax,
                            MapEndpointsInterfaceName
                        ),
                    transform: static (ctx, _) =>
                    {
                        var classSyntax = (ClassDeclarationSyntax)ctx.Node;
                        return ctx.SemanticModel.GetDeclaredSymbol(classSyntax)
                            as INamedTypeSymbol;
                    }
                )
                .Where(static symbol => symbol is not null)
                .Select(static (symbol, _) => symbol!);

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

            var compilationAndCandidates = context.CompilationProvider.Combine(
                projectOptions.Combine(
                    candidateClasses.Collect().Combine(razorDocuments.Collect())
                )
            );

            context.RegisterSourceOutput(
                compilationAndCandidates,
                static (spc, source) =>
                {
                    var (compilation, (projectOptions, (classCandidates, razorDocuments))) = source;

                    var mapEndpointsInterfaceSymbol = compilation.GetTypeByMetadataName(
                        MapEndpointsInterfaceMetadataName
                    );

                    if (mapEndpointsInterfaceSymbol is null)
                    {
                        return;
                    }

                    var endpointCandidates = new HashSet<string>(StringComparer.Ordinal);

                    foreach (
                        var candidate in classCandidates.Distinct(
                            SymbolEqualityComparer.Default
                        )
                    )
                    {
                        if (candidate is not INamedTypeSymbol typeSymbol)
                        {
                            continue;
                        }

                        var componentTypeName = MapEndpointsGenerator.TryCreateSymbolCandidate(
                            typeSymbol,
                            mapEndpointsInterfaceSymbol
                        );

                        if (componentTypeName is null)
                        {
                            continue;
                        }

                        endpointCandidates.Add(componentTypeName);
                    }

                    var razorProjectEngine = RazorComponentDiscovery.TryCreateProjectEngine(projectOptions);
                    if (razorProjectEngine is null)
                    {
                        spc.AddSource(
                            MapEndpointsSourceHintName,
                            SourceText.From(
                                CreateSource(
                                    endpointCandidates.OrderBy(name => name, StringComparer.Ordinal)
                                ),
                                System.Text.Encoding.UTF8
                            )
                        );
                        return;
                    }

                    foreach (var razorDocumentPath in razorDocuments.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        var componentTypeName = MapEndpointsGenerator.TryCreateRazorCandidate(
                            compilation,
                            mapEndpointsInterfaceSymbol,
                            razorProjectEngine,
                            projectOptions.ProjectDirectory,
                            razorDocumentPath
                        );

                        if (componentTypeName is null)
                        {
                            continue;
                        }

                        endpointCandidates.Add(componentTypeName);
                    }

                    spc.AddSource(
                        MapEndpointsSourceHintName,
                        SourceText.From(
                            CreateSource(
                                endpointCandidates.OrderBy(name => name, StringComparer.Ordinal)
                            ),
                            System.Text.Encoding.UTF8
                        )
                    );
                }
            );
        }

        private static string? TryCreateSymbolCandidate(
            INamedTypeSymbol typeSymbol,
            INamedTypeSymbol mapEndpointsInterfaceSymbol
        )
        {
            if (!GeneratorTypeHelpers.IsTopLevelConcreteNonGenericType(typeSymbol)
                || !GeneratorTypeHelpers.ImplementsInterface(typeSymbol, mapEndpointsInterfaceSymbol))
            {
                return null;
            }

            return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private static string? TryCreateRazorCandidate(
            Compilation compilation,
            INamedTypeSymbol mapEndpointsInterfaceSymbol,
            RazorProjectEngine razorProjectEngine,
            string? projectDirectory,
            string razorDocumentPath
        )
        {
            if (!RazorComponentDiscovery.TryGetGeneratedComponent(
                    compilation,
                    razorProjectEngine,
                    projectDirectory,
                    razorDocumentPath,
                    out var component)
                || component is null
                || !GeneratorTypeHelpers.ImplementsInterface(
                    component.Symbol,
                    mapEndpointsInterfaceSymbol
                ))
            {
                return null;
            }

            return component.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private static string CreateSource(IEnumerable<string> componentTypeNames)
        {
            var builder = new System.Text.StringBuilder();

            builder.AppendLine("#nullable enable");
            builder.AppendLine("using Microsoft.AspNetCore.Routing;");
            builder.AppendLine();
            builder.AppendLine("namespace BlazorBlades;");
            builder.AppendLine();
            builder.AppendLine("public static class ComponentEndpointRouteBuilderExtensions");
            builder.AppendLine("{");
            builder.AppendLine("    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder app)");
            builder.AppendLine("    {");
            foreach (var componentTypeName in componentTypeNames)
            {
                builder.Append("        ");
                builder.Append(componentTypeName);
                builder.AppendLine(".MapEndpoints(app);");
            }
            builder.AppendLine("        return app;");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            return builder.ToString();
        }

    }
}
