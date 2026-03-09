using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorBlades.Generators.Core
{
    internal static class GeneratorTypeHelpers
    {
        public static bool CouldBeInterfaceCandidate(
            ClassDeclarationSyntax classSyntax,
            string interfaceName
        ) => classSyntax.BaseList?.Types.Any(
            baseType =>
                baseType.Type switch
                {
                    IdentifierNameSyntax { Identifier.ValueText: var name } =>
                        string.Equals(name, interfaceName, StringComparison.Ordinal),
                    QualifiedNameSyntax
                    {
                        Right: IdentifierNameSyntax { Identifier.ValueText: var name }
                    } => string.Equals(name, interfaceName, StringComparison.Ordinal),
                    AliasQualifiedNameSyntax
                    {
                        Name: IdentifierNameSyntax { Identifier.ValueText: var name }
                    } => string.Equals(name, interfaceName, StringComparison.Ordinal),
                    _ => false,
                }
        ) == true;

        public static bool ImplementsInterface(
            INamedTypeSymbol typeSymbol,
            INamedTypeSymbol interfaceSymbol
        ) => typeSymbol.AllInterfaces.Any(@interface =>
            SymbolEqualityComparer.Default.Equals(@interface, interfaceSymbol)
        );

        public static bool IsTopLevelConcreteNonGenericType(INamedTypeSymbol typeSymbol) =>
            !typeSymbol.IsAbstract
            && typeSymbol.TypeParameters.Length == 0
            && typeSymbol.ContainingType is null;
    }
}
