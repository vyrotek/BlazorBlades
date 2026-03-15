using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorBlades.Generators.Core
{
    internal static class RazorComponentDiscovery
    {
        public static RazorProjectEngine? TryCreateProjectEngine(
            GeneratorProjectOptions projectOptions
        )
        {
            if (string.IsNullOrWhiteSpace(projectOptions.ProjectDirectory))
            {
                return null;
            }

            var fileSystem = RazorProjectFileSystem.Create(projectOptions.ProjectDirectory!);
            var configuration = RazorConfiguration.Create(
                RazorLanguageVersion.Latest,
                GeneratorConstants.RazorConfigurationName,
                Array.Empty<RazorExtension>(),
                useConsolidatedMvcViews: false
            );

            return RazorProjectEngine.Create(
                configuration,
                fileSystem,
                builder =>
                {
                    if (!string.IsNullOrWhiteSpace(projectOptions.RootNamespace))
                    {
                        RazorProjectEngineBuilderExtensions.SetRootNamespace(
                            builder,
                            projectOptions.RootNamespace!
                        );
                    }
                }
            );
        }

        public static bool TryGetGeneratedComponentSymbol(
            Compilation compilation,
            RazorProjectEngine razorProjectEngine,
            string? projectDirectory,
            string razorDocumentPath,
            out INamedTypeSymbol? componentSymbol
        )
        {
            componentSymbol = null;

            if (compilation is not CSharpCompilation csharpCompilation)
            {
                return false;
            }

            if (
                string.Equals(
                    Path.GetFileName(razorDocumentPath),
                    GeneratorConstants.ImportsRazorFileName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }

            var componentName = Path.GetFileNameWithoutExtension(razorDocumentPath);
            if (string.IsNullOrWhiteSpace(componentName))
            {
                return false;
            }

            var projectItemPath = TryGetProjectItemPath(projectDirectory, razorDocumentPath);
            if (string.IsNullOrWhiteSpace(projectItemPath))
            {
                return false;
            }

            var projectItem = razorProjectEngine.FileSystem.GetItem(projectItemPath);
            if (!projectItem.Exists)
            {
                return false;
            }

            var codeDocument = razorProjectEngine.Process(projectItem);
            var csharpDocument = RazorCodeDocumentExtensions.GetCSharpDocument(codeDocument);
            if (string.IsNullOrWhiteSpace(csharpDocument.GeneratedCode))
            {
                return false;
            }

            var parseOptions = csharpCompilation.SyntaxTrees
                .Select(static syntaxTree => syntaxTree.Options)
                .OfType<CSharpParseOptions>()
                .FirstOrDefault()
                ?? CSharpParseOptions.Default;

            var generatedSyntaxTree = CSharpSyntaxTree.ParseText(
                csharpDocument.GeneratedCode,
                parseOptions,
                (projectItem.PhysicalPath ?? razorDocumentPath)
                    + GeneratorConstants.GeneratedCSharpFileExtension
            );

            var probeCompilation = csharpCompilation.AddSyntaxTrees(generatedSyntaxTree);
            var semanticModel = probeCompilation.GetSemanticModel(generatedSyntaxTree);
            var componentDeclaration = generatedSyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(@class =>
                    @class.Identifier.ValueText == componentName
                    && @class.Modifiers.Any(static modifier =>
                        modifier.IsKind(SyntaxKind.PartialKeyword)
                    )
                );

            if (componentDeclaration is null)
            {
                return false;
            }

            componentSymbol = semanticModel.GetDeclaredSymbol(componentDeclaration) as INamedTypeSymbol;
            if (componentSymbol is null)
            {
                return false;
            }
            return true;
        }

        private static string? TryGetProjectItemPath(
            string? projectDirectory,
            string razorDocumentPath
        )
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                return null;
            }

            var relativePath = GetRelativePath(projectDirectory!, razorDocumentPath);
            if (string.IsNullOrWhiteSpace(relativePath)
                || relativePath.StartsWith("..", StringComparison.Ordinal))
            {
                return null;
            }

            return "/" + relativePath
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
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
    }

}
