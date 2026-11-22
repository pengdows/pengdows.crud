using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Xunit;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// The two SQLite extensions that add column types, loaded for real: SpatiaLite (geometry, win-x64 build only on NuGet) and sqlite-vec
/// (vec0 float vectors). Without them SQLite keeps POINT and VECTOR(3) as labels (SqliteDeclaredTypeNameTests); here the native types
/// are exercised. Each test opens its own in-memory connection, enables extension loading on it and loads the library by full path.
/// </summary>
public sealed class SqliteNativeExtensionTypeTests
{
    private static string NativePath(string rid, string file) =>
        Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", file);

    private static SqliteConnection Open(string library)
    {
        Assert.True(File.Exists(library), $"The native extension is missing from the test output: {library}");
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        connection.EnableExtensions(true);
        using var load = connection.CreateCommand();
        load.CommandText = "SELECT load_extension($path)";
        load.Parameters.AddWithValue("$path", library);
        load.ExecuteNonQuery();
        return connection;
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    private static void Run(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    // ---- SpatiaLite ------------------------------------------------------------------------------------------------------------------

    // mod_spatialite on NuGet ships Windows builds only, so elsewhere there is no binary to load.
    private static bool HasSpatiaLite => OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64;

    private static SqliteConnection OpenSpatiaLite()
    {
        // mod_spatialite.dll depends on libgeos, libiconv, zlib and the MinGW runtime beside it; load_extension does not search its own
        // folder for them, so the folder goes on the process PATH first.
        var folder = Path.GetDirectoryName(NativePath("win-x64", "mod_spatialite.dll"))!;
        if (!(Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Contains(folder, StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("PATH", folder + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
        }

        var connection = Open(NativePath("win-x64", "mod_spatialite.dll"));
        Run(connection, "SELECT InitSpatialMetaData(1)");
        return connection;
    }

    [Fact]
    public void SpatiaLite_PointColumn_RoundTripsThroughTextAndWkb()
    {
        if (!HasSpatiaLite)
        {
            return; // no SpatiaLite binary for this platform (see HasSpatiaLite); the type does not exist here
        }

        using var connection = OpenSpatiaLite();
        Run(connection, "CREATE TABLE places (id INTEGER PRIMARY KEY, name TEXT)");
        Assert.Equal(1L, Convert.ToInt64(Scalar(connection, "SELECT AddGeometryColumn('places', 'g', 4326, 'POINT', 'XY')")));
        Run(connection, "INSERT INTO places VALUES (1, 'a', GeomFromText('POINT(1.5 -2.25)', 4326))");

        Assert.Equal("POINT(1.5 -2.25)", Scalar(connection, "SELECT AsText(g) FROM places WHERE id = 1"));
        Assert.Equal("POINT", Scalar(connection, "SELECT GeometryType(g) FROM places WHERE id = 1"));
        Assert.Equal(4326L, Convert.ToInt64(Scalar(connection, "SELECT SRID(g) FROM places WHERE id = 1")));
        Assert.Equal(1.5, Convert.ToDouble(Scalar(connection, "SELECT ST_X(g) FROM places WHERE id = 1")));
        Assert.Equal(-2.25, Convert.ToDouble(Scalar(connection, "SELECT ST_Y(g) FROM places WHERE id = 1")));

        // The column holds SpatiaLite's own blob, not text; WKB goes out and comes back as the same point.
        Assert.Equal("blob", Scalar(connection, "SELECT typeof(g) FROM places WHERE id = 1"));
        var wkb = (byte[])Scalar(connection, "SELECT AsBinary(g) FROM places WHERE id = 1")!;
        Assert.Equal(21, wkb.Length); // 1 byte order + 4 type + 2 x 8 ordinates
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO places VALUES (2, 'b', GeomFromWKB($wkb, 4326))";
            insert.Parameters.AddWithValue("$wkb", wkb);
            insert.ExecuteNonQuery();
        }

        Assert.Equal("POINT(1.5 -2.25)", Scalar(connection, "SELECT AsText(g) FROM places WHERE id = 2"));
        Assert.Equal(1L, Convert.ToInt64(Scalar(connection, "SELECT Equals(a.g, b.g) FROM places a, places b WHERE a.id = 1 AND b.id = 2")));
    }

    [Fact]
    public void SpatiaLite_PlanarGeometry_PolygonKeepsItsShape()
    {
        if (!HasSpatiaLite)
        {
            return;
        }

        using var connection = OpenSpatiaLite();
        Run(connection, "CREATE TABLE areas (id INTEGER PRIMARY KEY)");
        Scalar(connection, "SELECT AddGeometryColumn('areas', 'g', 4326, 'POLYGON', 'XY')");
        Run(connection, "INSERT INTO areas VALUES (1, GeomFromText('POLYGON((0 0, 4 0, 4 3, 0 3, 0 0))', 4326))");

        Assert.Equal("POLYGON", Scalar(connection, "SELECT GeometryType(g) FROM areas"));
        Assert.Equal(12.0, Convert.ToDouble(Scalar(connection, "SELECT Area(g) FROM areas")));
        Assert.Equal(1L, Convert.ToInt64(Scalar(connection, "SELECT Contains(g, GeomFromText('POINT(1 1)', 4326)) FROM areas")));
        Assert.Equal(0L, Convert.ToInt64(Scalar(connection, "SELECT Contains(g, GeomFromText('POINT(9 9)', 4326)) FROM areas")));
    }

    [Fact]
    public void SpatiaLite_NullGeometry_ReadsBackAsNull()
    {
        if (!HasSpatiaLite)
        {
            return;
        }

        using var connection = OpenSpatiaLite();
        Run(connection, "CREATE TABLE n (id INTEGER PRIMARY KEY)");
        Scalar(connection, "SELECT AddGeometryColumn('n', 'g', 4326, 'POINT', 'XY')");
        Run(connection, "INSERT INTO n VALUES (1, NULL)");

        Assert.Null(Scalar(connection, "SELECT g FROM n WHERE id = 1"));
    }

    // ---- sqlite-vec ------------------------------------------------------------------------------------------------------------------

    private static SqliteConnection OpenVec()
    {
        var (rid, file) =
            OperatingSystem.IsWindows() ? ("win-x64", "vec0.dll") :
            OperatingSystem.IsLinux() ? (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64", "vec0.so") :
            (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64", "vec0.dylib");
        return Open(NativePath(rid, file));
    }

    private static byte[] Floats(params float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    [Fact]
    public void SqliteVec_Float32Vector_RoundTripsAsBlobAndAsJson()
    {
        using var connection = OpenVec();
        Run(connection, "CREATE VIRTUAL TABLE docs USING vec0(embedding float[3])");

        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO docs(rowid, embedding) VALUES (1, $v)";
            insert.Parameters.AddWithValue("$v", Floats(1.5f, -2.25f, 0.1f));
            insert.ExecuteNonQuery();
        }

        Run(connection, "INSERT INTO docs(rowid, embedding) VALUES (2, '[0, 0, 0]')"); // the JSON text form

        var back = (byte[])Scalar(connection, "SELECT embedding FROM docs WHERE rowid = 1")!;
        Assert.Equal(Floats(1.5f, -2.25f, 0.1f), back); // the float32 bytes come back bit for bit
        Assert.Equal("[1.500000,-2.250000,0.100000]", Scalar(connection, "SELECT vec_to_json(embedding) FROM docs WHERE rowid = 1"));
        Assert.Equal(Floats(0f, 0f, 0f), (byte[])Scalar(connection, "SELECT embedding FROM docs WHERE rowid = 2")!);
    }

    [Fact]
    public void SqliteVec_NearestNeighbour_FindsTheClosestVector()
    {
        using var connection = OpenVec();
        Run(connection, "CREATE VIRTUAL TABLE docs USING vec0(embedding float[3])");
        Run(connection, "INSERT INTO docs(rowid, embedding) VALUES (1, '[1, 0, 0]'), (2, '[0, 1, 0]'), (3, '[0, 0, 1]')");

        using var query = connection.CreateCommand();
        query.CommandText = "SELECT rowid, distance FROM docs WHERE embedding MATCH $q AND k = 2 ORDER BY distance";
        query.Parameters.AddWithValue("$q", Floats(0.9f, 0.1f, 0f));
        using var reader = query.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.True(reader.Read());
        Assert.Equal(2L, reader.GetInt64(0));
        Assert.False(reader.Read());
    }

    [Fact]
    public void SqliteVec_WrongDimension_IsRefused()
    {
        using var connection = OpenVec();
        Run(connection, "CREATE VIRTUAL TABLE docs USING vec0(embedding float[3])");

        var ex = Assert.Throws<SqliteException>(() =>
            Run(connection, "INSERT INTO docs(rowid, embedding) VALUES (1, '[1, 2]')"));
        Assert.Contains("dimension", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqliteVec_Int8AndBitVectors_RoundTrip()
    {
        using var connection = OpenVec();
        Run(connection, "CREATE VIRTUAL TABLE q USING vec0(a int8[4], b bit[8])");
        Run(connection, "INSERT INTO q(rowid, a, b) VALUES (1, vec_int8('[1, -2, 127, -128]'), vec_bit(X'A5'))");

        Assert.Equal("[1,-2,127,-128]", Scalar(connection, "SELECT vec_to_json(vec_int8(a)) FROM q WHERE rowid = 1"));
        Assert.Equal(new byte[] { 0xA5 }, (byte[])Scalar(connection, "SELECT b FROM q WHERE rowid = 1")!);
    }
}
