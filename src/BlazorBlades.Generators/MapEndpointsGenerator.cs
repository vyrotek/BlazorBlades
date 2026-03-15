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
                        && CouldBeMapEndpointsCandidate(classSyntax),
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
            if (!RazorComponentDiscovery.TryGetGeneratedComponentSymbol(
                    compilation,
                    razorProjectEngine,
                    projectDirectory,
                    razorDocumentPath,
                    out var componentSymbol)
                || componentSymbol is null
                || !GeneratorTypeHelpers.IsTopLevelConcreteNonGenericType(componentSymbol)
                || !GeneratorTypeHelpers.ImplementsInterface(
                    componentSymbol,
                    mapEndpointsInterfaceSymbol
                ))
            {
                return null;
            }

            return componentSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private static bool CouldBeMapEndpointsCandidate(ClassDeclarationSyntax classSyntax) =>
            classSyntax.BaseList?.Types.Any(
                baseType =>
                    baseType.Type switch
                    {
                        IdentifierNameSyntax { Identifier.ValueText: MapEndpointsInterfaceName } => true,
                        QualifiedNameSyntax
                        {
                            Right: IdentifierNameSyntax { Identifier.ValueText: MapEndpointsInterfaceName }
                        } => true,
                        AliasQualifiedNameSyntax
                        {
                            Name: IdentifierNameSyntax { Identifier.ValueText: MapEndpointsInterfaceName }
                        } => true,
                        _ => false,
                    }
            ) == true;

        private static string CreateSource(IEnumerable<string> componentTypeNames)
        {
            var mapEndpointCalls = string.Join(
                "\n",
                componentTypeNames.Select(componentTypeName =>
                    $"        {componentTypeName}.MapEndpoints(app);"
                )
            );

            var endpointMappings = string.IsNullOrEmpty(mapEndpointCalls)
                ? string.Empty
                : mapEndpointCalls + "\n";

            return $$"""
                #nullable enable
                using Microsoft.AspNetCore.Routing;

                namespace BlazorBlades;

                public static class ComponentEndpointRouteBuilderExtensions
                {
                    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder app)
                    {
                {{endpointMappings}}        return app;
                    }
                }
                """;
        }

    }
}
