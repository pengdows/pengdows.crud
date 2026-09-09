using pengdows.crud.dialects;
using pengdows.crud.enums;

namespace pengdows.crud.exceptions.translators;

internal interface IDbExceptionTranslator
{
    // Takes the dialect (not just its SupportedDatabase enum value) so constraint-kind
    // classification (Unique/ForeignKey/NotNull/Check) can delegate to the dialect's own
    // IsUniqueViolation/etc. overrides instead of maintaining a second, parallel signal-matching
    // switch here — see CLAUDE.md "Adding a New Database" checklist item 11/21 for the
    // duplicate-classification trap this closes (a live example: Sqlite's old translator used a
    // bare message-substring check that could disagree with SqliteDialect.IsUniqueViolation's
    // extended-result-code check).
    DatabaseException Translate(
        ISqlDialect dialect,
        Exception exception,
        DbOperationKind operationKind);
}
