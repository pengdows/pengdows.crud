#region

using System.Data;
using System.Reflection;
using System.Globalization;
using pengdows.crud.enums;

#endregion

namespace pengdows.crud.fakeDb;

/// <summary>
/// Specialized fake connection that emulates Npgsql.NpgsqlConnection behavior
/// for testing PostgreSQL dialect functionality
/// </summary>
public class fakeNpgsqlConnection : fakeDbConnection
{
    private bool _throwOnConnectionStringSet;

    public fakeNpgsqlConnection() : base()
    {
        EmulatedProduct = SupportedDatabase.PostgreSql;
        SetEmulatedTypeName("Npgsql.NpgsqlConnection");
    }

    /// <summary>
    /// Gets the type name that looks like Npgsql for connection type checking
    /// Since we can't override GetType(), we'll use a different approach
    /// </summary>
    public string GetNpgsqlTypeName()
    {
        return "Npgsql.NpgsqlConnection";
    }

    /// <summary>
    /// Sets whether to throw an exception when the ConnectionString property is set
    /// This simulates configuration failures during connection string modification
    /// </summary>
    public void SetThrowOnConnectionStringSet(bool shouldThrow)
    {
        _throwOnConnectionStringSet = shouldThrow;
    }

    public override string ConnectionString
    {
        get => base.ConnectionString;
        set
        {
            if (_throwOnConnectionStringSet && !string.IsNullOrEmpty(base.ConnectionString) && value != base.ConnectionString)
            {
                throw new InvalidOperationException("Npgsql configuration failed");
            }
            base.ConnectionString = value;
        }
    }
}