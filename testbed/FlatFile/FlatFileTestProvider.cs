using pengdows.crud;

namespace testbed.FlatFile;

/// <summary>
/// Runs the shared TestProvider suite against pengdows.flatfile. The engine parses ISO SQL only,
/// so the per-type DDL helpers in TestProvider carry FlatFile cases (e.g. TIMESTAMP rather than
/// the vendor DATETIME).
/// </summary>
public class FlatFileTestProvider : TestProvider
{
    public FlatFileTestProvider(IDatabaseContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }
}
