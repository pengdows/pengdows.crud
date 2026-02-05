using System.Data;
using pengdows.crud;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using Microsoft.Data.SqlClient;

[Table("film")]
public class Film
{
    [Id(false)]
    [Column("film_id", DbType.Int32)]
    public int Id { get; set; }

    [Column("title", DbType.String)]
    public string Title { get; set; } = string.Empty;

    [Column("length", DbType.Int32)]
    public int Length { get; set; }
}

var map = new TypeMapRegistry();
map.Register<Film>();

var cfg = new DatabaseContextConfiguration
{
    ConnectionString = "Server=test;Database=test;",
    ReadWriteMode = ReadWriteMode.ReadWrite,
    DbMode = DbMode.Standard
};

var ctx = new DatabaseContext(cfg, SqlClientFactory.Instance, null, map);
var helper = new TableGateway<Film, int>(ctx);

// Generate the SQL for RetrieveOneAsync
var container = helper.BuildRetrieve(new[] { 123 }, "f");

Console.WriteLine("pengdows.crud SQL:");
Console.WriteLine(container.Query.ToString());
Console.WriteLine();

Console.WriteLine("Parameters:");
foreach (var param in container.Parameters)
{
    Console.WriteLine($"  {param.ParameterName} = {param.Value} ({param.DbType})");
}
Console.WriteLine();

Console.WriteLine("Dapper SQL:");
Console.WriteLine("SELECT film_id as [Id], title as [Title], length as [Length] FROM film WHERE film_id=@id");