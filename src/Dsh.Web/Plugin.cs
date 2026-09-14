using Dsh.Runtime;
using Dsh.Plugins;

[assembly: DshPlugin("@deepseek-ai/dsh-web")]

namespace Dsh.Web;

public sealed class Plugin : IDshPlugin
{
    public string[] Inject => [];

    public IDisposable Apply(Context ctx, object? config) => new CallbackDisposable();

    private sealed class CallbackDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
