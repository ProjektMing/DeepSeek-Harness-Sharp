namespace Dsh.Runtime;

public sealed class RuntimeException : Exception
{
    public string Code { get; }

    public RuntimeException(string code, string? message = null)
        : base(message ?? code)
    {
        Code = code;
    }

    public const string InactiveEffect = "INACTIVE_EFFECT";
}
