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
}
