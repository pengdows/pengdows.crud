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

    /// <summary>
    /// CONFIRMED live: the container's database is created with the server's default
    /// DB_LOCALE/CLIENT_LOCALE (no explicit locale requested at CREATE DATABASE time), which
    /// this image resolves to a Latin-1-family locale — Western European accented characters
    /// (as in the base <see cref="TestProvider.RoundTripDescription"/>) round-trip fine, but
    /// Polish/Latin-2 characters fail with "ERROR [HY000] [Informix][Informix ODBC
    /// Driver]Inexact character conversion during translation." Overridden to Latin-1-safe text
    /// rather than attempting to reconfigure the container's locale, matching the pattern this
    /// property's own doc comment already describes for limited-charset databases.
    /// </summary>
    protected override string RoundTripFidelityUnicodeText =>
        "Héllø Wörld résumé café naïve ñ åæø";
}
