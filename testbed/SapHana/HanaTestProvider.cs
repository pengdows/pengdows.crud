using pengdows.crud;

namespace testbed.SapHana;

/// <summary>
/// Relies on <see cref="TestProvider"/>'s base implementation. Add SAP HANA-specific overrides
/// here once a live run identifies base DDL/type/capability assumptions that don't hold —
/// same pattern as <c>InformixTestProvider</c>.
/// </summary>
public class HanaTestProvider : TestProvider
{
    public HanaTestProvider(IDatabaseContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }
}
