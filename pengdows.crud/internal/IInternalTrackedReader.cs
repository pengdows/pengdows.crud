using System.Data.Common;

namespace pengdows.crud.@internal;

internal interface IInternalTrackedReader
{
    DbDataReader InnerReader { get; }

    /// <summary>
    /// The underlying DbCommand that produced this reader.
    /// May be null after the reader is disposed. Read before disposing.
    /// Used by the ReaderInsertedId plan to access provider-specific properties
    /// such as MySqlCommand.LastInsertedId after executing an INSERT.
    /// </summary>
    DbCommand? InnerCommand { get; }

    /// <summary>
    /// The type a column the provider reports no field type for is read as, or null
    /// (<c>SqlDialect.GetUnresolvedColumnType</c>, TYPE-016). Callers that read
    /// <see cref="InnerReader"/> directly read such a column through <c>UnresolvedColumnReader</c>.
    /// </summary>
    Type? GetUnresolvedColumnType(int ordinal);
}
