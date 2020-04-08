namespace GitP4Sync.Models
{
    public enum SubmitStatus{Submitted, Shelved, SubmitReady, InProgress, Error, MergeConflict, ReviewRequired, UnmappedUsers}
    public class PullStatus
    {
        public SubmitStatus Status;
        public long Id;

        //todo: update this
        public bool ActionRequired => Status == SubmitStatus.Error || Status == SubmitStatus.InProgress ||
                                      Status == SubmitStatus.SubmitReady;
    }
}
