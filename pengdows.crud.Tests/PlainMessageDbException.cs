using System.Data.Common;

namespace pengdows.crud.Tests;

/// <summary>
/// Minimal message-only <see cref="DbException"/> test double, for dialects/translators whose
/// real provider exception carries no discriminable numeric error code or SqlState at all
/// (confirmed live for both Firebird and Access — classification for these providers is pure
/// English message-text substring matching). Shared here instead of being declared privately per
/// test file — it was previously duplicated verbatim (sometimes under the name
/// <c>PlainDbException</c>) across <c>Db2DialectTests.cs</c>, <c>DuckDbConstraintViolationTests.cs</c>,
/// <c>AccessDialectTests.cs</c>, and <c>AccessTranslatorTests.cs</c>; those existing private
/// copies were left as-is (out of scope for the Access work that surfaced this), but any new
/// message-only exception test double should use this one instead of declaring another copy.
/// </summary>
internal sealed class PlainMessageDbException : DbException
{
    public PlainMessageDbException(string message) : base(message)
    {
    }
}
