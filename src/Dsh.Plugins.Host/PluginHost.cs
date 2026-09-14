using System.Runtime.Loader;

namespace Dsh.Plugins;

public sealed class PluginHost
{
    public PluginCatalog Catalog { get; } = new();

    public void ScanDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*.dll"))
        {
            var fileName = Path.GetFileName(file);
            if (!fileName.StartsWith("Dsh.", StringComparison.Ordinal)
                && !fileName.EndsWith(".Plugin.dll", StringComparison.Ordinal))
            {
                continue;
            }
            var fullPath = Path.GetFullPath(file);
            var assembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(
                candidate => string.Equals(candidate.GetName().Name, Path.GetFileNameWithoutExtension(fullPath), StringComparison.OrdinalIgnoreCase));
            if (assembly is null)
            {
                try
                {
                    assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
                }
                catch (PlatformNotSupportedException)
                {
                    // NativeAOT 不支持运行时 Assembly.Load*，跳过；插件由生成目录 RegisterGeneratedCatalog 注册。
                    continue;
                }
            }
            Catalog.RegisterAssembly(assembly);
        }

        RegisterGeneratedCatalog();
    }

    public void RegisterGeneratedCatalog()
    {
        foreach (var entry in DshPluginCatalogRegistry.Snapshot())
            Catalog.RegisterPlugin(entry.Package, entry.Implementation);
    }
}
