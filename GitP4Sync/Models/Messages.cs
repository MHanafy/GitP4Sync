namespace GitP4Sync.Models
{
        public static class Messages
        {
            public const string ShelveMsg = "Changes were shelved to";
            public const string SubmitReadyMsg = "Ready to submit, Click submit to continue";
            public const string SubmitMsg = "Changes were submitted to Perforce";
            public const string ErrorMsg = "Unexpected error";
            public const string MergeConflict = "Merge conflict - automatic merge not possible";
            public const string MergeConflictSummary = "Pull request can't be merged automatically, Please merge manually and update the branch to proceed";
            public const string ReviewRequired = "Code review required";
            public const string ReviewRequiredSummary = "An approved code review is required before changes can be submitted to Perforce";
            public const string UnmappedUsers = "Unmapped users - mapping required";
        }
}