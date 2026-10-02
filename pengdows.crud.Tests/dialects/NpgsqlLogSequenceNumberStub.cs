namespace NpgsqlTypes
{
    // Stands in for Npgsql's NpgsqlLogSequenceNumber, recognized by name.
    public readonly struct NpgsqlLogSequenceNumber
    {
        public NpgsqlLogSequenceNumber(ulong value) => Value = value;
        public ulong Value { get; }
    }
}
