namespace ThirdLife.Persistence;

public abstract class JobStoreException : Exception
{
    protected JobStoreException(string resultCode, string message)
        : base(message)
    {
        ResultCode = resultCode;
    }

    public string ResultCode { get; }
}

public sealed class JobStorePathException : JobStoreException
{
    internal JobStorePathException()
        : base("store_path_rejected", "The protected job-store path is not safe or does not have the required access controls.")
    {
    }
}

public sealed class JobStoreCorruptionException : JobStoreException
{
    internal JobStoreCorruptionException(string resultCode = "store_corrupt")
        : base(resultCode, "The job store failed an integrity check and was preserved without automatic repair or replacement.")
    {
    }
}

public sealed class JobStoreVersionException : JobStoreException
{
    internal JobStoreVersionException()
        : base("store_newer_schema", "The job store uses a newer unsupported schema and was not changed.")
    {
    }
}

public sealed class JobStoreConflictException : JobStoreException
{
    internal JobStoreConflictException()
        : base("store_record_conflict", "The proposed record conflicts with already committed job-store state.")
    {
    }
}

public sealed class JobStoreBusyException : JobStoreException
{
    internal JobStoreBusyException()
        : base("store_busy", "The job store is busy; no partial record was accepted.")
    {
    }
}

public sealed class JobStoreUnavailableException : JobStoreException
{
    internal JobStoreUnavailableException()
        : base("store_unavailable", "The job store is unavailable; no success was recorded.")
    {
    }
}
