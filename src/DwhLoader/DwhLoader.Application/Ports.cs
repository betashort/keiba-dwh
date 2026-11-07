namespace DwhLoader;

public interface ILedger
{
    IReadOnlyList<CompleteFile> ListComplete();

    IReadOnlyList<IndexedRecord> ReadIndex(CompleteFile file);

    byte[] ReadPayload(CompleteFile file, IndexedRecord record);

    IReadOnlyList<Ob14Member> ReadActiveOb14();
}

public interface IWarehouse
{
    bool SchemaReady();

    IReadOnlySet<long> IngestedIds();

    void BeginFile();

    void Write(DbRow row);

    void MarkFile(long id, string dataspec, string path);

    void CommitFile();

    void RollbackFile();

    void ReplaceOb14(IReadOnlyList<Ob14Member> members);
}

public interface ILoadLog
{
    void Info(string message);

    void FileIssue(long fileId, string dataspec, long? seq, string recordId, string reason);
}

public sealed class NullLoadLog : ILoadLog
{
    public void Info(string message)
    {
    }

    public void FileIssue(long fileId, string dataspec, long? seq, string recordId, string reason)
    {
    }
}
