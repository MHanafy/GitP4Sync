namespace GitP4Sync.Models
{
    public enum SubmitStatus{Submitted, Shelved, SubmitReady, InProgress, Error, MergeConflict, ReviewRequired, UnmappedUsers, SubmitRetry}

    public interface IPullStatus
    {
        SubmitStatus Status { get; }
        int? Retries { get; }
        long Id { get; }
        long PullNumber { get; }
    }

    public class PullStatus : IPullStatus
    {
        public SubmitStatus Status { get; }
        public int? Retries { get; }
        public long Id { get; }
        public long PullNumber { get; }

        public PullStatus(long id, long pullNumber, SubmitStatus status, int? retries)
        {
            Id = id;
            PullNumber = pullNumber;
            Status = status;
            Retries = retries;
        }
    }
}
