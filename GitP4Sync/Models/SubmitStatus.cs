using System.Collections.Generic;

namespace GitP4Sync.Models
{
    public enum SubmitStatus{Submitted, Shelved, SubmitReady, InProgress, Error, MergeConflict, ReviewRequired, UnmappedUsers, SubmitRetry, FailedChecks, PendingChecks}

    public interface IPullStatus
    {
        SubmitStatus Status { get; }
        int? Retries { get; }
        long Id { get; }
        long PullNumber { get; }
        IReadOnlyDictionary<string, bool?> Checks { get; }
    }

    public class PullStatus : IPullStatus
    {
        public SubmitStatus Status { get; }
        public int? Retries { get; }
        public long Id { get; }
        public long PullNumber { get; }
        public IReadOnlyDictionary<string,bool?> Checks { get; }

        public PullStatus(long id, long pullNumber, SubmitStatus status, int? retries, IReadOnlyDictionary<string,bool?> checks)
        {
            Id = id;
            PullNumber = pullNumber;
            Status = status;
            Retries = retries;
            Checks = checks;
        }
    }
}
