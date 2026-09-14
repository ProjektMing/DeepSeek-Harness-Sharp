namespace Dsh.Runtime;

public abstract class Service
{
    public string Name { get; }
    public Context Ctx { get; }

    protected Service(Context ctx, string name)
    {
        Ctx = ctx;
        Name = name;
        ctx.Provide(name, this, Check);
    }

    protected virtual bool Check() => true;
}
