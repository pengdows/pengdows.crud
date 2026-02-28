// =============================================================================
// FILE: SbLite.cs
// PURPOSE: Static factory for creating StringBuilderLite instances.
//
// AI SUMMARY:
// - Factory class for StringBuilderLite with convenient defaults.
// - DefaultStack constant: 1024 characters for stack-allocated buffer.
// - Create(): Returns new StringBuilderLite with provided scratch span.
// - Usage: var sb = SbLite.Create(stackalloc char[SbLite.DefaultStack]);
// - Internal helper used throughout pengdows.crud for efficient string building.
// =============================================================================

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