#nullable enable
namespace pengdows.crud.@internal;

internal static class SbLite
{
    public const int DefaultStack = 1024;

    public static StringBuilderLite Create(Span<char> scratch)
    {
        return new StringBuilderLite(scratch);
    }
}