namespace DwhLoader;

public sealed class DbRow
{
    public DbRow(string table, bool child = false)
    {
        Table = table;
        Child = child;
    }

    public string Table { get; }

    public bool Child { get; }

    public List<(string Name, object Value)> Columns { get; } = [];

    public void Add(string name, object value) => Columns.Add((name, value));
}

public sealed record CompleteFile(long Id, string Dataspec, string Path);

public sealed record IndexedRecord(
    long Seq,
    string RecordId,
    byte[] Sha256,
    long Offset,
    int Length,
    DateTimeOffset AcquiredUtc);

public sealed record Ob14Member(DateOnly MeetingDay, int Generation, string RecordId, byte[] Sha256);

public static class LoadTiming
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
}
