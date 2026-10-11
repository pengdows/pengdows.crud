using System;
using pengdows.crud.dialects;

namespace pengdows.crud.wrappers;

/// <summary>
/// What a <see cref="TrackedReader"/> takes from its dialect, held once per dialect and operation kind so that every reader holds
/// one reference to it rather than one per setting (a reader is allocated for every read; each setting was 8 B of it).
/// </summary>
internal sealed class ReaderDialectProfile
{
    internal ReaderDialectProfile(
        Func<Exception, Exception?>? readFailureTranslator,
        Func<Exception, bool>? isUnreadableStoredValue,
        SqlDialect? unresolvedColumnDialect,
        TypeCoercionOptions coercionOptions)
    {
        ReadFailureTranslator = readFailureTranslator;
        IsUnreadableStoredValue = isUnreadableStoredValue;
        UnresolvedColumnDialect = unresolvedColumnDialect;
        CoercionOptions = coercionOptions;
    }

    /// <summary>Translates a provider exception raised while fetching a row (null when it isn't a provider exception).</summary>
    internal Func<Exception, Exception?>? ReadFailureTranslator { get; }

    /// <summary><c>SqlDialect.IsUnreadableStoredValue</c>: a provider exception meaning the stored value has no .NET representation.</summary>
    internal Func<Exception, bool>? IsUnreadableStoredValue { get; }

    /// <summary>The dialect when it reads some columns the provider reports no field type for (<c>ReadsUnresolvedColumns</c>), else null.</summary>
    internal SqlDialect? UnresolvedColumnDialect { get; }

    /// <summary>The dialect's coercion options.</summary>
    internal TypeCoercionOptions CoercionOptions { get; }
}
