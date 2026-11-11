namespace DiskSpaceAnalyzer.Core;

// Flat file matches retain their full paths. No parent folder size is added to the result.
public sealed class SearchResults
{
    public string Query { get; }
    public List<ViewEntry> Files { get; }
    public long Bytes { get; }
    public long Unknown { get; }
    public long ExcludedFolders { get; }

    public SearchResults(string query, List<ViewEntry> files, long excludedFolders)
    {
        Query = query; ExcludedFolders = excludedFolders;
        files.Sort((a, b) =>
        {
            int size = b.Bytes.CompareTo(a.Bytes);
            return size != 0 ? size : StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path);
        });
        Files = files;
        foreach (var file in files)
        {
            if (file.SizeKnown) Bytes += file.Bytes;
            else Unknown++;
        }
    }

    public bool SameResults(SearchResults other) => ExcludedFolders == other.ExcludedFolders && Files.SequenceEqual(other.Files);
}
