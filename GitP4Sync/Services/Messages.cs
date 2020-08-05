using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GitP4Sync.Models;
using MHanafy.GithubClient;
using MHanafy.GithubClient.Models;
using MHanafy.GithubClient.Models.Github;
using Microsoft.Extensions.Options;
using Action = MHanafy.GithubClient.Models.Github.Action;
using PullRequest = GitP4Sync.Models.PullRequest;

namespace GitP4Sync.Services
{
    public partial class GithubService
    {
        public static class Messages
        {
            public const string Shelve = "Changes were shelved to Perforce";
            public const string SubmitReady = "Ready to submit, Click submit to continue";
            public const string SubmitReadySummary = "Changes are ready to be submitted to Perforce, Click submit to continue.";
            public const string Submit = "Changes were submitted to Perforce";
            public const string SubmitRetry = "Error submitting - retrying";
            public const string ErrorMsg = "Unexpected error";
            public const string MergeConflict = "Merge conflict - automatic merge not possible";
            public const string MergeConflictSummary = "Pull request can't be merged automatically, Please merge manually and update the branch to proceed";
            public const string ReviewRequired = "Code review required";
            public const string ReviewRequiredSummary = "An approved code review is required before changes can be submitted to Perforce";
            public const string UnmappedUsers = "Unmapped users - mapping required";
            public const string FailedChecks = "Required check(s) failed";
            public const string FailedChecksSummary = "One or more Required check(s) failed, fix them first to proceed.\r\n";
            public const string PendingChecks = "Waiting for required check(s)";
            public const string PendingChecksSummary = "Some required checks are still running, waiting for them to succeed...\r\n";
        }
        
    }
}