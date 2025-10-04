using System.Diagnostics.CodeAnalysis;
using pengdows.crud.fakeDb;

namespace Npgsql {
    public sealed class NpgsqlConnectionFake : fakeDbConnection {
        public NpgsqlConnectionFake() : base() {}
        public override string Database => "stub";
        public override string DataSource => "stub";
        public override string ServerVersion => "15.0";
    [AllowNull]
    public override string ConnectionString { get; set; } = string.Empty;
    }
}
