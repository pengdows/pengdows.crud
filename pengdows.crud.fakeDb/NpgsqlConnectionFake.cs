using pengdows.crud.fakeDb;
using System.Data;
using System.Data.Common;

namespace Npgsql {
    public sealed class NpgsqlConnectionFake : fakeDbConnection {
        public NpgsqlConnectionFake() : base() {}
        public override string Database => "stub";
        public override string DataSource => "stub";
        public override string ServerVersion => "15.0";
        public override string ConnectionString { get; set; } = string.Empty;
    }
}
