using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorBlades.Generators.Core
{
    internal static class GeneratorTypeHelpers
    {
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
