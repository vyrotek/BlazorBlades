using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace BlazorBlades.Generators
{
    [Generator]
    public class RenderPropsGenerator : IIncrementalGenerator
    {
        private static readonly Regex PropertyDeclarationRegex = new Regex(
            @"^public\s+(?:required\s+)?(?<type>.+?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{",
            RegexOptions.Compiled
        );

        private static readonly Regex DeclaredTypeRegex = new Regex(
            @"\b(?:record|class|struct|interface|enum)\s+(?:class\s+|struct\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
            RegexOptions.Compiled
        );

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var candidateClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) =>
                        node is ClassDeclarationSyntax classSyntax
                        && CouldBeRenderPropsCandidate(classSyntax),
                    transform: static (ctx, _) =>
                    {
                        var classSyntax = (ClassDeclarationSyntax)ctx.Node;
                        return ctx.SemanticModel.GetDeclaredSymbol(classSyntax)
                            as INamedTypeSymbol;
                    }
                )
                .Where(static symbol => symbol is not null);

            var projectOptions = context.AnalyzerConfigOptionsProvider.Select(
                static (options, _) => new RazorProjectOptions(
                    RenderPropsGenerator.GetGlobalOption(
                        options.GlobalOptions,
                        "build_property.MSBuildProjectDirectory"
                    ),
                    RenderPropsGenerator.GetGlobalOption(
                        options.GlobalOptions,
                        "build_property.RootNamespace"
                    )
                    ?? RenderPropsGenerator.GetGlobalOption(
                        options.GlobalOptions,
                        "build_property.AssemblyName"
                    )
                )
            );

            var razorDocuments = context.AdditionalTextsProvider
                .Where(static file => file.Path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
                .Select(static (file, cancellationToken) => RenderPropsGenerator.TryCreateRazorDocument(file, cancellationToken))
                .Where(static document => document is not null)
                .Select(static (document, _) => document!);

            var compilationAndCandidates = context.CompilationProvider
                .Combine(projectOptions
                    .Combine(candidateClasses.Collect()
                        .Combine(razorDocuments.Collect())));

            context.RegisterSourceOutput(
                compilationAndCandidates,
                static (spc, source) =>
                {
                    var (compilation, (projectOptions, (classCandidates, razorDocuments))) = source;

                    var renderPropsInterfaceSymbol = compilation.GetTypeByMetadataName("BlazorBlades.IRenderProps");

                    if (renderPropsInterfaceSymbol is null)
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

                        var renderCandidate = RenderPropsGenerator.TryCreateSymbolCandidate(typeSymbol, renderPropsInterfaceSymbol);

                        if (renderCandidate is null || !generatedTypes.Add(renderCandidate.ComponentTypeName))
                        {
                            continue;
                        }

                        RenderPropsGenerator.AddSource(spc, renderCandidate);
                    }

                    var importNamespaces = razorDocuments
                        .Where(document =>
                            string.Equals(
                                Path.GetFileName(document.Path),
                                "_Imports.razor",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .Where(document =>
                            !string.IsNullOrWhiteSpace(document.Directory)
                            && !string.IsNullOrWhiteSpace(document.NamespaceDirective)
                        )
                        .GroupBy(document => document.Directory!, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            group => group.Key,
                            group => group.First().NamespaceDirective!,
                            StringComparer.OrdinalIgnoreCase
                        );

                    foreach (
                        var renderCandidate in razorDocuments
                            .Select(document =>
                                RenderPropsGenerator.TryCreateRazorCandidate(
                                    document,
                                    projectOptions,
                                    importNamespaces
                                )
                            )
                            .Where(candidate => candidate is not null)
                            .Select(candidate => candidate!)
                            .GroupBy(candidate => candidate.ComponentTypeName)
                            .Select(group => group.First())
                    )
                    {
                        if (!generatedTypes.Add(renderCandidate.ComponentTypeName))
                        {
                            continue;
                        }

                        RenderPropsGenerator.AddSource(spc, renderCandidate);
                    }
                }
            );
        }

        private static RenderCandidate? TryCreateSymbolCandidate(INamedTypeSymbol typeSymbol, INamedTypeSymbol renderPropsInterfaceSymbol)
        {
            if (typeSymbol.IsAbstract || typeSymbol.TypeParameters.Length > 0)
            {
                return null;
            }

            if (typeSymbol.ContainingType is not null)
            {
                return null;
            }

            if (!ImplementsRenderPropsInterface(typeSymbol, renderPropsInterfaceSymbol))
            {
                return null;
            }

            var componentTypeName = typeSymbol.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );

            var props = GetRequiredParameters(typeSymbol)
                .Select(property =>
                    new RenderProperty(
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
                typeSymbol.Name + "Props",
                props,
                GetAccessibilityKeyword(typeSymbol.DeclaredAccessibility),
                GetHintName(componentTypeName)
            );
        }

        private static RazorDocument? TryCreateRazorDocument(
            AdditionalText additionalText,
            CancellationToken cancellationToken
        )
        {
            var text = additionalText.GetText(cancellationToken)?.ToString();

            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var lines = ReadLines(text!).ToArray();
            var declaredTypeNames = new List<string>();
            var hasRenderPropsInterface = false;
            string? namespaceDirective = null;

            foreach (var line in lines)
            {
                var trimmedStart = line.TrimStart();

                if (!hasRenderPropsInterface
                    && trimmedStart.StartsWith("@implements", StringComparison.Ordinal)
                    && trimmedStart.IndexOf("IRenderProps", StringComparison.Ordinal) >= 0)
                {
                    hasRenderPropsInterface = true;
                }

                if (namespaceDirective is null
                    && trimmedStart.StartsWith("@namespace", StringComparison.Ordinal))
                {
                    var ns = trimmedStart.Substring("@namespace".Length).Trim();
                    namespaceDirective = ns.Length == 0 ? null : ns;
                }

                if (TryGetDeclaredTypeName(line, out var declaredTypeName))
                {
                    declaredTypeNames.Add(declaredTypeName);
                }
            }

            return new RazorDocument(
                additionalText.Path,
                lines,
                hasRenderPropsInterface,
                namespaceDirective,
                declaredTypeNames.ToArray()
            );
        }

        private static RenderCandidate? TryCreateRazorCandidate(
            RazorDocument razorDocument,
            RazorProjectOptions projectOptions,
            IDictionary<string, string> importNamespaces
        )
        {
            if (
                string.Equals(
                    Path.GetFileName(razorDocument.Path),
                    "_Imports.razor",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return null;
            }

            if (!razorDocument.HasRenderPropsInterface)
            {
                return null;
            }

            var componentName = Path.GetFileNameWithoutExtension(razorDocument.Path);
            if (string.IsNullOrWhiteSpace(componentName))
            {
                return null;
            }

            var props = GetRequiredParameters(razorDocument, componentName).ToArray();
            if (props.Length == 0)
            {
                return null;
            }

            var ns = ResolveNamespace(razorDocument, projectOptions, importNamespaces);
            var componentTypeName = string.IsNullOrWhiteSpace(ns)
                ? $"global::{componentName}"
                : $"global::{ns}.{componentName}";

            return new(
                string.IsNullOrWhiteSpace(ns) ? null : ns,
                componentName,
                componentTypeName,
                componentName + "Props",
                props,
                "public ",
                GetHintName(componentTypeName)
            );
        }

        private static bool CouldBeRenderPropsCandidate(ClassDeclarationSyntax classSyntax) =>
            classSyntax.BaseList?.Types.Any(
                static baseType =>
                    baseType.Type switch
                    {
                        IdentifierNameSyntax { Identifier.ValueText: "IRenderProps" } => true,
                        QualifiedNameSyntax
                        {
                            Right: IdentifierNameSyntax { Identifier.ValueText: "IRenderProps" }
                        } => true,
                        AliasQualifiedNameSyntax
                        {
                            Name: IdentifierNameSyntax { Identifier.ValueText: "IRenderProps" }
                        } => true,
                        _ => false,
                    }
            ) == true;

        private static bool ImplementsRenderPropsInterface(
            INamedTypeSymbol typeSymbol,
            INamedTypeSymbol renderPropsInterfaceSymbol
        ) => typeSymbol.AllInterfaces.Any(@interface =>
            SymbolEqualityComparer.Default.Equals(@interface, renderPropsInterfaceSymbol)
        );

        private static IEnumerable<IPropertySymbol> GetRequiredParameters(
            INamedTypeSymbol typeSymbol
        ) => typeSymbol.GetMembers().OfType<IPropertySymbol>().Where(static property =>
            IsRequiredParameter(property)
        );

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

                if (!hasParameter && string.Equals(attributeName, "ParameterAttribute", StringComparison.Ordinal))
                {
                    hasParameter = true;
                }
                else if (!hasEditorRequired && string.Equals(attributeName, "EditorRequiredAttribute", StringComparison.Ordinal))
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

        private static string CreateSource(RenderCandidate candidate) => $$"""
            #nullable enable
            {{(candidate.Namespace is null ? "" : $"namespace {candidate.Namespace};")}}
            {{candidate.Accessibility}}record {{candidate.PropsTypeName}}({{CreatePropsParameterList(candidate.Properties)}});
            {{candidate.Accessibility}}partial class {{candidate.TypeName}}
            {
                public static global::Microsoft.AspNetCore.Http.HttpResults
                    .RazorComponentResult<{{candidate.ComponentTypeName}}> Render(
                        {{candidate.PropsTypeName}} props)
                {
                    return new(props);
                }
            }
            """;

        private static string CreatePropsParameterList(
            IReadOnlyList<RenderProperty> properties
        ) => string.Join(", ", properties.Select(static property => $"{property.TypeName} {property.Name}"));

        private static void AddSource(
            SourceProductionContext sourceProductionContext,
            RenderCandidate candidate
        ) => sourceProductionContext.AddSource(
            candidate.HintName,
            SourceText.From(CreateSource(candidate), System.Text.Encoding.UTF8)
        );

        private static string? GetGlobalOption(
            AnalyzerConfigOptions options,
            string key
        ) => options.TryGetValue(key, out var value) ? value : null;

        private static IEnumerable<RenderProperty> GetRequiredParameters(
            RazorDocument razorDocument,
            string componentName
        )
        {
            var attributes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var line in razorDocument.Lines)
            {
                var trimmed = line.Trim();

                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (trimmed.StartsWith("[", StringComparison.Ordinal))
                {
                    foreach (var attributeName in GetAttributeNames(trimmed))
                    {
                        attributes.Add(attributeName);
                    }

                    trimmed = RemoveLeadingAttributeLists(trimmed).TrimStart();

                    if (trimmed.Length == 0)
                    {
                        continue;
                    }
                }

                if (TryGetProperty(trimmed, out var propertyName, out var typeName))
                {
                    if (
                        attributes.Contains("Parameter")
                        && attributes.Contains("EditorRequired")
                    )
                    {
                        yield return new RenderProperty(
                            propertyName,
                            QualifyRazorType(
                                componentName,
                                typeName,
                                razorDocument.DeclaredTypeNames
                            )
                        );
                    }

                    attributes.Clear();
                    continue;
                }

                attributes.Clear();
            }
        }

        private static IEnumerable<string> GetAttributeNames(string line)
        {
            var startIndex = 0;

            while (startIndex < line.Length)
            {
                var openBracketIndex = line.IndexOf('[', startIndex);

                if (openBracketIndex < 0)
                {
                    yield break;
                }

                var closeBracketIndex = line.IndexOf(']', openBracketIndex + 1);

                if (closeBracketIndex < 0)
                {
                    yield break;
                }

                var attributeList = line.Substring(
                    openBracketIndex + 1,
                    closeBracketIndex - openBracketIndex - 1
                );

                foreach (var attribute in attributeList.Split(','))
                {
                    var attributeName = attribute.Trim();
                    var argumentIndex = attributeName.IndexOf('(');

                    if (argumentIndex >= 0)
                    {
                        attributeName = attributeName.Substring(0, argumentIndex).Trim();
                    }

                    if (attributeName.EndsWith("Attribute", StringComparison.Ordinal))
                    {
                        attributeName = attributeName.Substring(
                            0,
                            attributeName.Length - "Attribute".Length
                        );
                    }

                    if (attributeName.Length > 0)
                    {
                        yield return attributeName;
                    }
                }

                startIndex = closeBracketIndex + 1;
            }
        }

        private static bool TryGetProperty(string line, out string propertyName, out string typeName)
        {
            var match = PropertyDeclarationRegex.Match(line);

            if (!match.Success)
            {
                propertyName = string.Empty;
                typeName = string.Empty;
                return false;
            }

            propertyName = match.Groups["name"].Value;
            typeName = match.Groups["type"].Value.Trim();
            return true;
        }

        private static string RemoveLeadingAttributeLists(string line)
        {
            var index = 0;

            while (index < line.Length)
            {
                while (index < line.Length && char.IsWhiteSpace(line[index]))
                {
                    index++;
                }

                if (index >= line.Length || line[index] != '[')
                {
                    break;
                }

                var closeBracketIndex = line.IndexOf(']', index + 1);

                if (closeBracketIndex < 0)
                {
                    return string.Empty;
                }

                index = closeBracketIndex + 1;
            }

            return line.Substring(index);
        }

        private static bool TryGetDeclaredTypeName(string line, out string declaredTypeName)
        {
            var match = DeclaredTypeRegex.Match(line.Trim());

            if (!match.Success)
            {
                declaredTypeName = string.Empty;
                return false;
            }

            declaredTypeName = match.Groups["name"].Value;
            return true;
        }

        private static string? ResolveNamespace(
            RazorDocument razorDocument,
            RazorProjectOptions projectOptions,
            IDictionary<string, string> importNamespaces
        )
        {
            var explicitNamespace = razorDocument.NamespaceDirective;

            if (!string.IsNullOrWhiteSpace(explicitNamespace))
            {
                return explicitNamespace;
            }

            if (string.IsNullOrWhiteSpace(razorDocument.Directory))
            {
                return projectOptions.RootNamespace;
            }

            var fileDirectory = razorDocument.Directory!;

            if (!string.IsNullOrWhiteSpace(projectOptions.ProjectDirectory))
            {
                var projectDirectory = projectOptions.ProjectDirectory!;
                var currentDirectory = fileDirectory;

                while (true)
                {
                    if (importNamespaces.TryGetValue(currentDirectory, out var importsNamespace))
                    {
                        var suffix = GetNamespaceSuffix(currentDirectory, fileDirectory);
                        return CombineNamespace(importsNamespace, suffix);
                    }

                    if (
                        string.Equals(
                            currentDirectory,
                            projectDirectory,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        break;
                    }

                    var parentDirectory = Path.GetDirectoryName(currentDirectory);

                    if (string.IsNullOrWhiteSpace(parentDirectory))
                    {
                        break;
                    }

                    currentDirectory = parentDirectory;
                }

                return CombineNamespace(
                    projectOptions.RootNamespace,
                    GetNamespaceSuffix(projectDirectory, fileDirectory)
                );
            }

            return projectOptions.RootNamespace;
        }

        private static IEnumerable<string> ReadLines(string text)
        {
            using var reader = new StringReader(text);
            string? line;

            while ((line = reader.ReadLine()) is not null)
            {
                yield return line;
            }
        }

        private static string? GetNamespaceSuffix(string baseDirectory, string targetDirectory)
        {
            var relativePath = GetRelativePath(baseDirectory, targetDirectory);

            if (string.IsNullOrEmpty(relativePath) || relativePath == ".")
            {
                return null;
            }

            return relativePath
                .Replace(Path.DirectorySeparatorChar, '.')
                .Replace(Path.AltDirectorySeparatorChar, '.');
        }

        private static string? CombineNamespace(
            string? baseNamespace,
            string? suffix
        )
        {
            if (string.IsNullOrWhiteSpace(baseNamespace))
            {
                return string.IsNullOrWhiteSpace(suffix) ? null : suffix;
            }

            if (string.IsNullOrWhiteSpace(suffix))
            {
                return baseNamespace;
            }

            return $"{baseNamespace}.{suffix}";
        }

        private static string QualifyRazorType(
            string componentName,
            string typeName,
            IReadOnlyList<string> declaredTypeNames
        )
        {
            var qualifiedTypeName = typeName;

            foreach (var declaredTypeName in declaredTypeNames)
            {
                qualifiedTypeName = Regex.Replace(
                    qualifiedTypeName,
                    $@"(?<![A-Za-z0-9_\.:]){Regex.Escape(declaredTypeName)}(?![A-Za-z0-9_])",
                    componentName + "." + declaredTypeName
                );
            }

            return qualifiedTypeName;
        }

        private static string GetRelativePath(string basePath, string targetPath)
        {
            var baseUri = new Uri(AppendDirectorySeparator(basePath));
            var targetUri = new Uri(targetPath);
            var relativeUri = baseUri.MakeRelativeUri(targetUri);

            return Uri.UnescapeDataString(relativeUri.ToString())
                .Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparator(string path) =>
            path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;

        private static string GetHintName(string metadataName)
        {
            return metadataName
                .Replace("global::", string.Empty)
                .Replace('<', '[')
                .Replace('>', ']')
                .Replace('.', '_')
                .Replace(':', '_')
                + ".Renderer.g.cs";
        }

        private sealed class RazorProjectOptions
        {
            public RazorProjectOptions(string? projectDirectory, string? rootNamespace)
            {
                ProjectDirectory = projectDirectory;
                RootNamespace = rootNamespace;
            }

            public string? ProjectDirectory { get; }

            public string? RootNamespace { get; }
        }

        private sealed class RazorDocument
        {
            public RazorDocument(
                string path,
                IReadOnlyList<string> lines,
                bool hasRenderPropsInterface,
                string? namespaceDirective,
                IReadOnlyList<string> declaredTypeNames
            )
            {
                Path = path;
                Directory = System.IO.Path.GetDirectoryName(path);
                Lines = lines;
                HasRenderPropsInterface = hasRenderPropsInterface;
                NamespaceDirective = namespaceDirective;
                DeclaredTypeNames = declaredTypeNames;
            }

            public string Path { get; }

            public string? Directory { get; }

            public IReadOnlyList<string> Lines { get; }

            public bool HasRenderPropsInterface { get; }

            public string? NamespaceDirective { get; }

            public IReadOnlyList<string> DeclaredTypeNames { get; }
        }

        private sealed class RenderCandidate
        {
            public RenderCandidate(
                string? @namespace,
                string typeName,
                string componentTypeName,
                string propsTypeName,
                IReadOnlyList<RenderProperty> properties,
                string accessibility,
                string hintName
            )
            {
                Namespace = @namespace;
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

            public IReadOnlyList<RenderProperty> Properties { get; }

            public string Accessibility { get; }

            public string HintName { get; }
        }

        private sealed class RenderProperty
        {
            public RenderProperty(string name, string typeName)
            {
                Name = name;
                TypeName = typeName;
            }

            public string Name { get; }

            public string TypeName { get; }
        }
    }
}