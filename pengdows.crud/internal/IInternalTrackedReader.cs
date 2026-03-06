using System.Data.Common;

namespace pengdows.crud.@internal;

internal interface IInternalTrackedReader
{
    DbDataReader InnerReader { get; }
}
