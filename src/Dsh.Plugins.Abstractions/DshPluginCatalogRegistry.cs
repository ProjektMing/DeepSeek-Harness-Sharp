using System.Diagnostics.CodeAnalysis;

namespace Dsh.Plugins;

public static class DshPluginCatalogRegistry
{
    private static readonly List<DshPluginCatalogEntry> Entries = [];
    private static readonly Lock Gate = new();

    public static void Register(
        string package,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementation)
    {
        lock (Gate)
        {
            if (Entries.Any(entry => entry.Package == package))
                return;
            Entries.Add(new DshPluginCatalogEntry(package, implementation));
        }
    }

    public static IReadOnlyList<DshPluginCatalogEntry> Snapshot()
    {
        lock (Gate)
            return [.. Entries];
    }
}

public sealed record DshPluginCatalogEntry(
    string Package,
    [property: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type Implementation);
