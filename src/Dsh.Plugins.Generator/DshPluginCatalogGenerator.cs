using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Dsh.Plugins.Generator;

[Generator(LanguageNames.CSharp)]
public sealed partial class DshPluginCatalogGenerator : IIncrementalGenerator
{
    private const string InterfaceMetadataName = "Dsh.Plugins.IDshPlugin";
    private const string AttributeMetadataName = "Dsh.Plugins.DshPluginAttribute";
    private const string HintName = "DshPluginCatalog.g.cs";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entries = context.CompilationProvider.Select(static (compilation, _) => GetPluginEntries(compilation));
        context.RegisterSourceOutput(entries, static (context, value) =>
        {
            context.AddSource(HintName, SourceText.From(GenerateSource(value), Encoding.UTF8));
        });
    }

    private static List<PluginEntry> GetPluginEntries(Compilation compilation)
    {
        var pluginInterface = compilation.GetTypeByMetadataName(InterfaceMetadataName);
        var pluginAttribute = compilation.GetTypeByMetadataName(AttributeMetadataName);
        if (pluginInterface is null || pluginAttribute is null)
            return [];

        var assemblies = new List<IAssemblySymbol> { compilation.Assembly };
        assemblies.AddRange(compilation.SourceModule.ReferencedAssemblySymbols);

        var entries = new List<PluginEntry>();
        foreach (var assembly in assemblies.Distinct<IAssemblySymbol>(SymbolEqualityComparer.Default))
        {
            var packages = assembly.GetAttributes()
                .Where(attribute => attribute.AttributeClass is not null && SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, pluginAttribute))
                .Select(attribute => attribute.ConstructorArguments.FirstOrDefault().Value as string)
                .Where(package => !string.IsNullOrEmpty(package))
                .ToList();
            if (packages.Count == 0)
                continue;

            foreach (var type in EnumerateTypes(assembly.GlobalNamespace))
            {
                if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic)
                    continue;
                if (!ImplementsPlugin(type, pluginInterface))
                    continue;
                foreach (var package in packages)
                {
                    entries.Add(new PluginEntry(
                        package!,
                        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                }
            }
        }

        return entries
            .Distinct()
            .OrderBy(entry => entry.Package, System.StringComparer.Ordinal)
            .ThenBy(entry => entry.TypeName, System.StringComparer.Ordinal)
            .ToList();
    }

    private static bool ImplementsPlugin(INamedTypeSymbol type, INamedTypeSymbol pluginInterface)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.AllInterfaces.Any(interfaceType => SymbolEqualityComparer.Default.Equals(interfaceType, pluginInterface)))
                return true;
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
            yield return type;
        foreach (var child in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (var type in EnumerateTypes(child))
                yield return type;
        }
    }

    private static string GenerateSource(List<PluginEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Templates.Header.Replace("{capacity}", entries.Count.ToString()));
        foreach (var entry in entries)
        {
            builder.AppendLine(Templates.EntryLine
                .Replace("{package}", entry.Package)
                .Replace("{typeName}", entry.TypeName));
        }

        builder.AppendLine(Templates.Footer);
        return builder.ToString();
    }

    private sealed class PluginEntry
    {
        public PluginEntry(string package, string typeName)
        {
            Package = package;
            TypeName = typeName;
        }

        public string Package { get; }

        public string TypeName { get; }
    }
}