using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace BlazorBlades.Generators
{
    [Generator]
    public class MapEndpointsGenerator : IIncrementalGenerator
    {
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
                static (options, _) => new RazorProjectOptions(
                    GetGlobalOption(
                        options.GlobalOptions,
                        "build_property.MSBuildProjectDirectory"
                    ),
                    GetGlobalOption(
                        options.GlobalOptions,
                        "build_property.RootNamespace"
                    )
                    ?? GetGlobalOption(
                        options.GlobalOptions,
                        "build_property.AssemblyName"
                    )
                )
            );

            var razorDocuments = context.AdditionalTextsProvider
                .Where(static file =>
                    file.Path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                )
                .Select(
                    static (file, cancellationToken) =>
                        TryCreateRazorDocument(file, cancellationToken)
                )
                .Where(static document => document is not null)
                .Select(static (document, _) => document!);

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
                        "BlazorBlades.IMapEndpoints"
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

                    var importNamespaces = razorDocuments
                        .Where(document =>
                            string.Equals(
                                Path.GetFileName(document.Path),
                                "_Imports.razor",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .Select(document => new
                        {
                            Directory = Path.GetDirectoryName(document.Path),
                            Namespace = document.Namespace
                        })
                        .Where(entry =>
                            !string.IsNullOrWhiteSpace(entry.Directory)
                            && !string.IsNullOrWhiteSpace(entry.Namespace)
                        )
                        .GroupBy(entry => entry.Directory!, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            group => group.Key,
                            group => group.First().Namespace!,
                            StringComparer.OrdinalIgnoreCase
                        );

                    foreach (
                        var componentTypeName in razorDocuments
                            .Select(document =>
                                TryCreateRazorCandidate(
                                    document,
                                    projectOptions,
                                    importNamespaces
                                )
                            )
                            .Where(candidate => candidate is not null)
                            .Select(candidate => candidate!)
                    )
                    {
                        endpointCandidates.Add(componentTypeName);
                    }

                    spc.AddSource(
                        "BlazorBlades_Core_MapEndpoints.g.cs",
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
            if (typeSymbol.IsAbstract || typeSymbol.TypeParameters.Length > 0)
            {
                return null;
            }

            if (typeSymbol.ContainingType is not null)
            {
                return null;
            }

            if (!ImplementsMapEndpointsInterface(typeSymbol, mapEndpointsInterfaceSymbol))
            {
                return null;
            }

            return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private static RazorDocument? TryCreateRazorDocument(
            AdditionalText additionalText,
            System.Threading.CancellationToken cancellationToken
        )
        {
            var text = additionalText.GetText(cancellationToken)?.ToString();

            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string? ns = null;
            var implementsMapEndpoints = false;

            foreach (var line in ReadLines(text!))
            {
                var trimmed = line.TrimStart();

                ns ??= TryParseNamespaceDirective(trimmed);
                implementsMapEndpoints |= IsMapEndpointsImplementsDirective(trimmed);

                if (ns is not null && implementsMapEndpoints)
                {
                    break;
                }
            }

            return new RazorDocument(additionalText.Path, ns, implementsMapEndpoints);
        }

        private static string? TryCreateRazorCandidate(
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

            if (!razorDocument.ImplementsMapEndpoints)
            {
                return null;
            }

            var componentName = Path.GetFileNameWithoutExtension(razorDocument.Path);

            if (string.IsNullOrWhiteSpace(componentName))
            {
                return null;
            }

            var ns = ResolveNamespace(
                razorDocument.Path,
                razorDocument.Namespace,
                projectOptions,
                importNamespaces
            );
            return string.IsNullOrWhiteSpace(ns)
                ? $"global::{componentName}"
                : $"global::{ns}.{componentName}";
        }

        private static bool CouldBeMapEndpointsCandidate(
            ClassDeclarationSyntax classSyntax
        ) => classSyntax.BaseList is not null;

        private static bool ImplementsMapEndpointsInterface(
            INamedTypeSymbol typeSymbol,
            INamedTypeSymbol mapEndpointsInterfaceSymbol
        ) => typeSymbol.AllInterfaces.Any(@interface =>
            SymbolEqualityComparer.Default.Equals(@interface, mapEndpointsInterfaceSymbol)
        );

        private static bool IsMapEndpointsImplementsDirective(string trimmedLine)
        {
            if (!trimmedLine.StartsWith("@implements", StringComparison.Ordinal))
            {
                return false;
            }

            var interfaceName = trimmedLine.Substring("@implements".Length).Trim();
            var matchIndex = interfaceName.LastIndexOf("IMapEndpoints", StringComparison.Ordinal);

            if (matchIndex < 0)
            {
                return false;
            }

            var matchEnd = matchIndex + "IMapEndpoints".Length;

            if (matchEnd < interfaceName.Length && !char.IsWhiteSpace(interfaceName[matchEnd]))
            {
                return false;
            }

            return matchIndex == 0
                || interfaceName[matchIndex - 1] == '.'
                || interfaceName[matchIndex - 1] == ':';
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

        private static string? GetGlobalOption(
            AnalyzerConfigOptions options,
            string key
        ) => options.TryGetValue(key, out var value) ? value : null;

        private static string? ResolveNamespace(
            string razorFilePath,
            string? explicitNamespace,
            RazorProjectOptions projectOptions,
            IDictionary<string, string> importNamespaces
        )
        {
            if (!string.IsNullOrWhiteSpace(explicitNamespace))
            {
                return explicitNamespace;
            }

            var fileDirectory = Path.GetDirectoryName(razorFilePath);

            if (string.IsNullOrWhiteSpace(fileDirectory))
            {
                return projectOptions.RootNamespace;
            }

            var projectDirectory = projectOptions.ProjectDirectory;

            if (!string.IsNullOrWhiteSpace(projectDirectory))
            {
                var currentDirectory = fileDirectory;

                while (!string.IsNullOrWhiteSpace(currentDirectory))
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

                    currentDirectory = Path.GetDirectoryName(currentDirectory);
                }

                return CombineNamespace(
                    projectOptions.RootNamespace,
                    GetNamespaceSuffix(projectDirectory!, fileDirectory)
                );
            }

            return projectOptions.RootNamespace;
        }

        private static string? TryParseNamespaceDirective(string trimmedLine)
        {
            if (!trimmedLine.StartsWith("@namespace", StringComparison.Ordinal))
            {
                return null;
            }

            var ns = trimmedLine.Substring("@namespace".Length).Trim();
            return ns.Length == 0 ? null : ns;
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

        private static string? CombineNamespace(string? baseNamespace, string? suffix)
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
            public RazorDocument(string path, string? ns, bool implementsMapEndpoints)
            {
                Path = path;
                Namespace = ns;
                ImplementsMapEndpoints = implementsMapEndpoints;
            }

            public string Path { get; }

            public string? Namespace { get; }

            public bool ImplementsMapEndpoints { get; }
        }
    }
}
