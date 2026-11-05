namespace pengdows.crud.fakeDb;

/// <summary>
/// A stored Oracle BFILE as ODP.NET exposes it: a locator (the directory object and the file name) and the file's
/// content. On a BFILE column <see cref="fakeDbDataReader.GetValue"/> returns the content's bytes, which is what the
/// driver returns (confirmed live), and <see cref="fakeDbDataReader.GetProviderSpecificValue"/> returns this locator.
/// </summary>
public readonly struct fakeDbBFile
{
    public fakeDbBFile(string directoryName, string fileName, byte[] content)
    {
        DirectoryName = directoryName;
        FileName = fileName;
        Content = content;
    }

    /// <summary>The directory object the file is in.</summary>
    public string DirectoryName { get; }

    /// <summary>The file name within the directory.</summary>
    public string FileName { get; }

    /// <summary>The file's bytes, as GetValue returns them.</summary>
    public byte[] Content { get; }
}
