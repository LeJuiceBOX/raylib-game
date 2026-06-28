using System.IO.Compression;

public class PmapArchive : IDisposable
{
    private readonly ZipArchive _archive;

    public PmapArchive(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _archive = new ZipArchive(stream, ZipArchiveMode.Read);
    }

    /// <summary>
    /// Reads the content of a specific file within the archive as a string.
    /// </summary>
    /// <param name="entryPath">The path within the archive, e.g. "data/config.xml"</param>
    public string ReadText(string entryPath)
    {
        var entry = GetEntry(entryPath);
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Reads the content of a specific file within the archive as raw bytes.
    /// </summary>
    public byte[] ReadBytes(string entryPath)
    {
        var entry = GetEntry(entryPath);
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Opens a stream for a specific file within the archive.
    /// Caller is responsible for disposing the stream.
    /// </summary>
    public Stream OpenStream(string entryPath)
    {
        var entry = GetEntry(entryPath);
        return entry.Open();
    }

    /// <summary>
    /// Returns true if the given path exists within the archive.
    /// </summary>
    public bool EntryExists(string entryPath) =>
        _archive.GetEntry(NormalizePath(entryPath)) != null;

    /// <summary>
    /// Lists all file paths within the archive.
    /// </summary>
    public IEnumerable<string> ListEntries() =>
        _archive.Entries.Select(e => e.FullName);

    private ZipArchiveEntry GetEntry(string entryPath)
    {
        var normalized = NormalizePath(entryPath);
        return _archive.GetEntry(normalized)
            ?? throw new FileNotFoundException($"Entry not found in archive: {normalized}");
    }

    // ZipArchive uses forward slashes internally
    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    public void Dispose() => _archive.Dispose();
}
