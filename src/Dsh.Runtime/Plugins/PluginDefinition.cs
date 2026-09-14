namespace Dsh.Runtime;

public sealed record PluginDefinition
{
    public string? Name { get; init; }
    public IReadOnlyList<string> Inject { get; init; } = [];
    public required Func<Context, object?, object?> Apply { get; init; }

    public static PluginDefinition From(
        Func<Context, object?, object?> apply,
        string? name = null,
        IReadOnlyList<string>? inject = null)
    {
        return new PluginDefinition
        {
            Name = name ?? apply.Method.Name,
            Inject = inject ?? [],
            Apply = apply,
        };
    }
}

public enum ActivationState
{
    Pending,
    Activating,
    Active,
    Deactivating,
    Failed,
    Disposed,
}
