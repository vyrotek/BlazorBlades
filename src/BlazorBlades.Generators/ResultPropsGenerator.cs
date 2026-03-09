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
    public class ResultPropsGenerator : IIncrementalGenerator
    {
        private const string EditorRequiredAttributeName = "EditorRequiredAttribute";
        private const string GlobalAliasPrefix = "global::";
        private const string ParameterAttributeName = "ParameterAttribute";
        private const string PropsTypeSuffix = "Props";
        private const string ResultPropsInterfaceName = "IResultProps";
        private const string ResultPropsInterfaceMetadataName = "BlazorBlades.IResultProps";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var candidateClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) =>
                        node is ClassDeclarationSyntax classSyntax
                        && GeneratorTypeHelpers.CouldBeInterfaceCandidate(
                            classSyntax,
                            ResultPropsInterfaceName
                        ),
                    transform: static (ctx, _) =>
                    {
                        var classSyntax = (ClassDeclarationSyntax)ctx.Node;
                        return ctx.SemanticModel.GetDeclaredSymbol(classSyntax)
                            as INamedTypeSymbol;
                    }
                )
                .Where(static symbol => symbol is not null);

            var projectOptions = context.AnalyzerConfigOptionsProvider.Select(
                static (options, _) => GeneratorProjectOptions.Create(options.GlobalOptions)
            );

            var razorDocuments = context.AdditionalTextsProvider
                .Where(static file => file.Path.EndsWith(
                    GeneratorConstants.RazorFileExtension,
                    StringComparison.OrdinalIgnoreCase))
                .Select(static (file, _) => file.Path);

            var compilationAndCandidates = context.CompilationProvider
                .Combine(projectOptions
                    .Combine(candidateClasses.Collect()
                        .Combine(razorDocuments.Collect())));

            context.RegisterSourceOutput(
                compilationAndCandidates,
                (spc, source) =>
                {
                    var (compilation, (projectOptions, (classCandidates, razorDocuments))) = source;

                    var resultPropsInterfaceSymbol = compilation.GetTypeByMetadataName(ResultPropsInterfaceMetadataName);

                    if (resultPropsInterfaceSymbol is null)
                    {
                        return;
                    }

                    var generatedTypes = new HashSet<string>(StringComparer.Ordinal);

                    foreach (var candidate in classCandidates.Distinct(SymbolEqualityComparer.Default))
                    {
                        if (candidate is not INamedTypeSymbol typeSymbol)
                        {
                            continue;
                        }

                        var resultCandidate = ResultPropsGenerator.TryCreateSymbolCandidate(typeSymbol, resultPropsInterfaceSymbol);

                        if (resultCandidate is null || !generatedTypes.Add(resultCandidate.ComponentTypeName))
                        {
                            continue;
                        }

                        ResultPropsGenerator.AddSource(spc, resultCandidate);
                    }

                    var razorProjectEngine = RazorComponentDiscovery
                        .TryCreateProjectEngine(projectOptions);
                    if (razorProjectEngine is null)
                    {
                        return;
                    }

                    foreach (var razorDocumentPath in razorDocuments.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        var resultCandidate = ResultPropsGenerator.TryCreateRazorCandidate(
                            compilation,
                            resultPropsInterfaceSymbol,
                            razorProjectEngine,
                            projectOptions.ProjectDirectory,
                            razorDocumentPath
                        );

                        if (resultCandidate is null || !generatedTypes.Add(resultCandidate.ComponentTypeName))
                        {
                            continue;
                        }

                        ResultPropsGenerator.AddSource(spc, resultCandidate);
                    }
                }
            );
        }

        private static ResultCandidate? TryCreateSymbolCandidate(INamedTypeSymbol typeSymbol, INamedTypeSymbol resultPropsInterfaceSymbol)
        {
            if (!GeneratorTypeHelpers.IsTopLevelConcreteNonGenericType(typeSymbol)
                || !GeneratorTypeHelpers.ImplementsInterface(typeSymbol, resultPropsInterfaceSymbol))
            {
                return null;
            }

            var componentTypeName = typeSymbol.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );

            var props = GetRequiredParameters(typeSymbol)
                .Select(property =>
                    new ResultProperty(
                        property.Name,
                        property.Type.ToDisplayString(
                            SymbolDisplayFormat.FullyQualifiedFormat
                        )
                    )
                )
                .ToArray();

            if (props.Length == 0)
            {
                return null;
            }

            var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : typeSymbol.ContainingNamespace.ToDisplayString();

            return new(
                ns,
                typeSymbol.Name,
                componentTypeName,
                typeSymbol.Name + PropsTypeSuffix,
                props,
                GetAccessibilityKeyword(typeSymbol.DeclaredAccessibility),
                GetHintName(componentTypeName)
            );
        }

        private static ResultCandidate? TryCreateRazorCandidate(
            Compilation compilation,
            INamedTypeSymbol resultPropsInterfaceSymbol,
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
                    resultPropsInterfaceSymbol
                ))
            {
                return null;
            }

            var props = GetRequiredParameters(component.SemanticModel, component.Declaration)
                .Select(property =>
                    new ResultProperty(
                        property.Name,
                        property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    )
                )
                .ToArray();

            if (props.Length == 0)
            {
                return null;
            }

            var ns = component.Symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : component.Symbol.ContainingNamespace.ToDisplayString();
            var componentTypeName = component.Symbol.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );

            return new(
                ns,
                component.Symbol.Name,
                componentTypeName,
                component.Symbol.Name + PropsTypeSuffix,
                props,
                GetAccessibilityKeyword(component.Symbol.DeclaredAccessibility),
                GetHintName(componentTypeName)
            );
        }

        private static IEnumerable<IPropertySymbol> GetRequiredParameters(
            INamedTypeSymbol typeSymbol
        ) => typeSymbol.GetMembers().OfType<IPropertySymbol>().Where(static property =>
            IsRequiredParameter(property)
        );

        private static IEnumerable<IPropertySymbol> GetRequiredParameters(
            SemanticModel semanticModel,
            ClassDeclarationSyntax classDeclaration
        ) => classDeclaration.Members
            .OfType<PropertyDeclarationSyntax>()
            .Select(propertyDeclaration =>
                semanticModel.GetDeclaredSymbol(propertyDeclaration) as IPropertySymbol
            )
            .Where(static property => property is not null)
            .Select(static property => property!)
            .Where(static property => IsRequiredParameter(property));

        private static bool IsRequiredParameter(IPropertySymbol property)
        {
            if (property.IsStatic
                || property.DeclaredAccessibility != Accessibility.Public
                || property.SetMethod is null)
            {
                return false;
            }

            var hasParameter = false;
            var hasEditorRequired = false;

            foreach (var attribute in property.GetAttributes())
            {
                var attributeName = attribute.AttributeClass?.Name;

                if (!hasParameter && string.Equals(attributeName, ParameterAttributeName, StringComparison.Ordinal))
                {
                    hasParameter = true;
                }
                else if (!hasEditorRequired && string.Equals(attributeName, EditorRequiredAttributeName, StringComparison.Ordinal))
                {
                    hasEditorRequired = true;
                }

                if (hasParameter && hasEditorRequired)
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetAccessibilityKeyword(Accessibility accessibility) =>
            accessibility switch
            {
                Accessibility.Public => "public ",
                Accessibility.Internal => "internal ",
                _ => string.Empty,
            };

        private static string CreateSource(ResultCandidate candidate)
        {
            var namespaceDeclaration = candidate.Namespace is null
                ? string.Empty
                : $"namespace {candidate.Namespace};\n";

            return $$"""
                #nullable enable
                {{namespaceDeclaration}}{{candidate.Accessibility}}record {{candidate.PropsTypeName}}({{CreatePropsParameterList(candidate.Properties)}});
                {{candidate.Accessibility}}partial class {{candidate.TypeName}}
                {
                    public static global::Microsoft.AspNetCore.Http.HttpResults
                        .RazorComponentResult<{{candidate.TypeName}}>Result({{candidate.PropsTypeName}} props, string? contentType = null, int? statusCode = null)
                        {
                            return new(props)
                            {
                                PreventStreamingRendering = true,
                                ContentType = contentType,
                                StatusCode = statusCode
                            };
                        }
                }
                """;
        }

        private static string CreatePropsParameterList(
            IReadOnlyList<ResultProperty> properties
        ) => string.Join(", ", properties.Select(static property => $"{property.TypeName} {property.Name}"));

        private static void AddSource(
            SourceProductionContext sourceProductionContext,
            ResultCandidate candidate
        ) => sourceProductionContext.AddSource(
            candidate.HintName,
            SourceText.From(CreateSource(candidate), System.Text.Encoding.UTF8)
        );

        private static string GetHintName(string metadataName)
        {
            return metadataName
                .Replace(GlobalAliasPrefix, string.Empty)
                .Replace('<', '[')
                .Replace('>', ']')
                .Replace('.', '_')
                .Replace(':', '_')
                + GeneratorConstants.GeneratedCSharpFileExtension;
        }

        private sealed class ResultCandidate
        {
            public ResultCandidate(
                string? ns,
                string typeName,
                string componentTypeName,
                string propsTypeName,
                IReadOnlyList<ResultProperty> properties,
                string accessibility,
                string hintName
            )
            {
                Namespace = ns;
                TypeName = typeName;
                ComponentTypeName = componentTypeName;
                PropsTypeName = propsTypeName;
                Properties = properties;
                Accessibility = accessibility;
                HintName = hintName;
            }

            public string? Namespace { get; }

            public string TypeName { get; }

            public string ComponentTypeName { get; }

            public string PropsTypeName { get; }

            public IReadOnlyList<ResultProperty> Properties { get; }

            public string Accessibility { get; }

            public string HintName { get; }
        }

        private sealed class ResultProperty
        {
            public ResultProperty(string name, string typeName)
            {
                Name = name;
                TypeName = typeName;
            }

            public string Name { get; }

            public string TypeName { get; }
        }
    }
}