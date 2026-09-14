using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Dsh.Plugins;

/** 协作式卸载验证:发起 Unload() 并返回弱引用;调用方必须先丢弃全部强引用,再调用 WaitForCollection。 */
public static class PluginUnloader
{
    public const int MaxGcRounds = 10;

    public static WeakReference Unload(AssemblyLoadContext context)
    {
        var weak = new WeakReference(context, trackResurrection: true);
        context.Unload();
        return weak;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool WaitForCollection(WeakReference weak, out string? report)
    {
        for (var round = 0; round < MaxGcRounds && weak.IsAlive; round++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            if (weak.IsAlive)
                Thread.Sleep(10);
        }
        if (weak.IsAlive)
        {
            report = $"load context is still referenced after {MaxGcRounds} GC rounds";
            return false;
        }
        report = null;
        return true;
    }
}
