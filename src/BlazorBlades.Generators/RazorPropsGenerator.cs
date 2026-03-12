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
    public class RazorPropsGenerator : IIncrementalGenerator
    {
        private const string EditorRequiredAttributeName = "EditorRequiredAttribute";
        private const string GlobalAliasPrefix = "global::";
        private const string ParameterAttributeName = "ParameterAttribute";
        private const string PropsTypeSuffix = "Props";
        private const string RazorPropsInterfaceName = "IRazorProps";
        private const string RazorPropsInterfaceMetadataName = "BlazorBlades.IRazorProps";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var candidateClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) =>
                        node is ClassDeclarationSyntax classSyntax
                        && GeneratorTypeHelpers.CouldBeInterfaceCandidate(
                            classSyntax,
                            RazorPropsInterfaceName
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

                    var resultPropsInterfaceSymbol = compilation.GetTypeByMetadataName(RazorPropsInterfaceMetadataName);

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

                        var resultCandidate = RazorPropsGenerator.TryCreateSymbolCandidate(typeSymbol, resultPropsInterfaceSymbol);

                        if (resultCandidate is null || !generatedTypes.Add(resultCandidate.ComponentTypeName))
                        {
                            continue;
                        }

                        RazorPropsGenerator.AddSource(spc, resultCandidate);
                    }

                    var razorProjectEngine = RazorComponentDiscovery
                        .TryCreateProjectEngine(projectOptions);
                    if (razorProjectEngine is null)
                    {
                        return;
                    }

                    foreach (var razorDocumentPath in razorDocuments.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        var resultCandidate = RazorPropsGenerator.TryCreateRazorCandidate(
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

                        RazorPropsGenerator.AddSource(spc, resultCandidate);
                    }
                }
            );
        }

        private static RazorCandidate? TryCreateSymbolCandidate(INamedTypeSymbol typeSymbol, INamedTypeSymbol resultPropsInterfaceSymbol)
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
                    new RazorProperty(
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

        private static RazorCandidate? TryCreateRazorCandidate(
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
                    new RazorProperty(
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

        private static string CreateSource(RazorCandidate candidate)
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

                    public static async global::System.Threading.Tasks.Task<string> RenderAsync(global::System.IServiceProvider services, {{candidate.PropsTypeName}} props)
                    {
                        var loggerFactory = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                            .GetRequiredService<global::Microsoft.Extensions.Logging.ILoggerFactory>(services);
                        await using var renderer = new global::Microsoft.AspNetCore.Components.Web.HtmlRenderer(services, loggerFactory);
                        var root = await renderer.Dispatcher.InvokeAsync(
                            () => renderer.RenderComponentAsync<{{candidate.TypeName}}>(
                                global::Microsoft.AspNetCore.Components.ParameterView.FromDictionary(
                                    {{CreateRenderParameterDictionary(candidate.Properties)}}
                                )
                            )
                        );
                        await root.QuiescenceTask;
                        return await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
                    }
                }
                """;
        }

        private static string CreatePropsParameterList(
            IReadOnlyList<RazorProperty> properties
        ) => CreatePropertyList(
            properties,
            static property => $"{property.TypeName} {property.Name}"
        );

        private static string CreateRenderParameterDictionary(
            IReadOnlyList<RazorProperty> properties
        ) => $"new global::System.Collections.Generic.Dictionary<string, object?> {{ {CreatePropertyList(properties, static property => $"[\"{property.Name}\"] = props.{property.Name}")} }}";

        private static string CreatePropertyList(
            IReadOnlyList<RazorProperty> properties,
            Func<RazorProperty, string> selector
        ) => string.Join(", ", properties.Select(selector));

        private static void AddSource(
            SourceProductionContext sourceProductionContext,
            RazorCandidate candidate
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

        private sealed class RazorCandidate
        {
            public RazorCandidate(
                string? ns,
                string typeName,
                string componentTypeName,
                string propsTypeName,
                IReadOnlyList<RazorProperty> properties,
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

            public IReadOnlyList<RazorProperty> Properties { get; }

            public string Accessibility { get; }

            public string HintName { get; }
        }

        private sealed class RazorProperty
        {
            public RazorProperty(string name, string typeName)
            {
                Name = name;
                TypeName = typeName;
            }

            public string Name { get; }

            public string TypeName { get; }
        }
    }
}