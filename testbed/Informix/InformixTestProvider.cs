using pengdows.crud;

namespace testbed.Informix;

/// <summary>
/// Relies mostly on <see cref="TestProvider"/>'s base implementation (including the base
/// <c>CreateTable()</c>, now that <c>GetDateTimeType</c> has an Informix-specific case). Once a
/// live run confirms (or corrects) further base DDL/type choices, this is the place to add the
/// same kind of provider-specific regression coverage <c>Db2TestProvider</c> has for Db2 (e.g. a
/// real unique-constraint exception-type check, MERGE parameter binding).
/// </summary>
public class InformixTestProvider : TestProvider
{
    public InformixTestProvider(IDatabaseContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }

    // CONFIRMED live against icr.io/informix/informix-developer-database:latest: the container's
    // default database codeset cannot represent non-ASCII characters ("Inexact character
    // conversion during translation" on INSERT) - no DB_LOCALE/CLIENT_LOCALE env var or setup
    // script hook exists on this image to configure a UTF-8 locale at database-creation time, so
    // (like Firebird's own NONE-charset default) this is a real container-default limitation, not
    // a pengdows.crud gap. Same ASCII-only override pattern as Firebird/Sybase.
    protected override string RoundTripDescription => "Hello World ASCII round-trip test string";
    protected override string RoundTripFidelityUnicodeText => "Hello World ASCII fidelity test string";
}
