using System;
using System.Collections.Generic;
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
    public class GithubService : IGithubService
    {
        public static class Messages
        {
            public const string ShelveMsg = "Changes were shelved to Perforce";
            public const string SubmitReadyMsg = "Ready to submit, Click submit to continue";
            public const string SubmitReadySummary = "Changes are ready to be submitted to Perforce, Click submit to continue.";
            public const string SubmitMsg = "Changes were submitted to Perforce";
            public const string SubmitRetry = "Error submitting - retrying";
            public const string ErrorMsg = "Unexpected error";
            public const string MergeConflict = "Merge conflict - automatic merge not possible";
            public const string MergeConflictSummary = "Pull request can't be merged automatically, Please merge manually and update the branch to proceed";
            public const string ReviewRequired = "Code review required";
            public const string ReviewRequiredSummary = "An approved code review is required before changes can be submitted to Perforce";
            public const string UnmappedUsers = "Unmapped users - mapping required";
        }

        private readonly GithubSettings _settings;
        private readonly IGithubClient _client;
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public GithubService(IGithubClient client, IOptions<GithubSettings> settings)
        {
            _settings = settings.Value;
            _client = client;
        }

        public async Task<InstallationToken> GetToken(long installationId)
        {
            return await _client.GetInstallationToken(installationId);
        }

        public async Task<IPullRequest> GetPullRequest(InstallationToken token, string repo, long number)
        {
             return PullRequest.CreateFrom(await _client.GetPullRequest(token, repo, number));
        }

        public async Task<IEnumerable<IPullRequest>> GetPullRequests(InstallationToken token, string repo)
        {
            return (await _client.GetPullRequests(token, repo)).Select(PullRequest.CreateFrom);
        }

        public async Task<(bool Valid, string ReviewerLogin)> ValidatePull(InstallationToken token, string repo, IPullRequest pull, IPullStatus pullStatus)
        {
            if (pull.Mergeable == null)
            {
                //retrieve the pull again to check for the flag when null
                var updatedPull = await GetPullRequest(token, repo, pull.Number);
                if (updatedPull.Mergeable == null)
                {
                    //if still null assume invalid
                    Logger.Info($"Null mergable flag Skipping pull '{pull.Number}' - Status '{pullStatus.Status}'");
                    return (false, null);
                }

                pull.Mergeable = updatedPull.Mergeable;
            }

            if (!pull.Mergeable.Value)
            {
                //Only update status if required
                if(pullStatus.Status != SubmitStatus.MergeConflict) await UpdatePullStatus(token, repo, pullStatus, SubmitStatus.MergeConflict);
                return (false, null);
            }

            var review =
                (await _client.GetReviews(token, repo, pull.Number)).FirstOrDefault(x =>
                    x.State == Review.ReviewState.Approved);

            return (true, review?.User.Login);
        }

        public async Task UpdatePullStatus(InstallationToken token, string repo, long statusId,string[] unmappedUsers)
        {
            var title = Messages.UnmappedUsers;
            var conclusion = CheckRun.RunConclusion.ActionRequired;
            var users = unmappedUsers.Length == 1
                ? $"user '{unmappedUsers[0]}' isn't"
                : $"users '{string.Join("', '", unmappedUsers[..^1])}' and '{unmappedUsers[^1]}' aren't";
            var summary = $"Github {users} mapped and couldn't be mapped automatically; Please update users file to continue";
            await _client.UpdateCheckRun(token, repo, statusId, CheckRun.RunStatus.Completed, conclusion,
                new CheckRunOutput {Title = title, Summary = summary});
            Logger.Info($"Status updated - {conclusion} {summary}");
        }

        public async Task UpdatePullStatus(InstallationToken token, string repo, long statusId, Exception ex)
        {
            var title = Messages.ErrorMsg;
            var conclusion = CheckRun.RunConclusion.ActionRequired;
            var summary = $"Operation failed\r\n{ex.Message}";
            await _client.UpdateCheckRun(token, repo, statusId, CheckRun.RunStatus.Completed, conclusion,
                new CheckRunOutput {Title = title, Summary = summary});
            Logger.Info($"Status updated - {conclusion}");
        }

        public async Task UpdatePullStatus(InstallationToken token, string repo, IPullStatus status, Exception ex, bool showSubmit, int? retries, int maxRetries)
        {
            //When showing the submit button, update the title to let the user take action
            var title = showSubmit ? Messages.ErrorMsg : Messages.SubmitRetry;
            var conclusion = CheckRun.RunConclusion.ActionRequired;
            var retryStr = retries == null ? null : $"[retry {retries} out of {maxRetries}]";
            var submitMsg = showSubmit ? " click submit to retry again" : null;
            var summary = $"Operation failed {retryStr}{submitMsg}\r\n{ex.Message}";
            var actions = showSubmit?new List<Action> {GetSubmitAction(status.PullNumber)}:null;
            await _client.UpdateCheckRun(token, repo, status.Id, CheckRun.RunStatus.Completed, conclusion,
                new CheckRunOutput { Title = title, Summary = summary }, DateTime.UtcNow, actions);
            Logger.Info($"Status updated - {conclusion} {retryStr}");
        }

        private Action GetSubmitAction(long pullNumber)
        {
            return new Action
            {
                Label = "Submit to Perforce", Identifier = pullNumber.ToString(),
                Description = "Submit changes and close pull request"
            };
        }

        public async Task UpdatePullStatus(InstallationToken token, string repo, long statusId, string changeList, bool submitted, bool userAutoSubmit)
        {
            string title;
            var conclusion = submitted ? CheckRun.RunConclusion.Success : CheckRun.RunConclusion.ActionRequired;
            string summary;
            if (submitted)
            {
                summary = $"{Messages.SubmitMsg}; changelist '{changeList}'";
                title = Messages.SubmitMsg;
            }
            else
            {
                summary = userAutoSubmit
                    ? $"AutoSubmit is disabled, {Messages.ShelveMsg} changelist '{changeList}' "
                    : $"{Messages.ShelveMsg} changelist '{changeList}', Contact administrator to enable AutoSubmit for your account";
                title = Messages.ShelveMsg;
            }
            
            await _client.UpdateCheckRun(token, repo, statusId, CheckRun.RunStatus.Completed, conclusion,
                new CheckRunOutput {Title = title, Summary = summary});
            Logger.Info($"Status updated - {conclusion} {summary}");
        }

        public async Task UpdatePullStatus(InstallationToken token, string repo, IPullStatus status, SubmitStatus submitStatus)
        {
            CheckRunOutput output;
            string conclusion;
            string runStatus;
            List<Action> actions = null;
            switch (submitStatus)
            {
                case SubmitStatus.MergeConflict:
                    output = new CheckRunOutput{Title = Messages.MergeConflict, Summary = Messages.MergeConflictSummary};
                    conclusion = CheckRun.RunConclusion.ActionRequired;
                    runStatus = CheckRun.RunStatus.Completed;
                    break;
                case SubmitStatus.Submitted:
                    throw new ArgumentOutOfRangeException(nameof(submitStatus), submitStatus,
                        "Submit isn't supported by this overload");
                    break;
                case SubmitStatus.Shelved:
                    throw new ArgumentOutOfRangeException(nameof(submitStatus), submitStatus,
                        "Shelve isn't supported by this overload");
                    break;
                case SubmitStatus.SubmitReady:
                    output = new CheckRunOutput{Title = Messages.SubmitReadyMsg, Summary = Messages.SubmitReadySummary};
                    conclusion = CheckRun.RunConclusion.ActionRequired;
                    runStatus = CheckRun.RunStatus.Completed;
                    actions = new List<Action> {GetSubmitAction(status.PullNumber)};
                    break;
                case SubmitStatus.ReviewRequired:
                    output = new CheckRunOutput{Title = Messages.ReviewRequired, Summary = Messages.ReviewRequiredSummary};
                    conclusion = CheckRun.RunConclusion.ActionRequired;
                    runStatus = CheckRun.RunStatus.Completed;
                    break;
                case SubmitStatus.InProgress:
                    runStatus = CheckRun.RunStatus.InProgress;
                    conclusion = null;
                    output = null;
                    break;
                case SubmitStatus.UnmappedUsers:
                    throw new ArgumentOutOfRangeException(nameof(submitStatus), submitStatus,
                        "UnmappedUsers isn't supported by this overload");
                    break;
                case SubmitStatus.Error:
                    throw new ArgumentOutOfRangeException(nameof(submitStatus), submitStatus,
                        "Error isn't supported by this overload");
                    break;
                case SubmitStatus.SubmitRetry:
                    throw new ArgumentOutOfRangeException(nameof(submitStatus), submitStatus,
                        "SubmitRetry isn't supported by this overload");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(submitStatus), submitStatus, "unexpected submit status");
            }
            await _client.UpdateCheckRun(token, repo, status.Id, runStatus, conclusion, output, DateTime.UtcNow, actions);
            Logger.Info($"Status updated - {conclusion} {output?.Summary}");
        }

        public async Task ClosePullRequest(InstallationToken token, string repo, long number)
        {
            await _client.ClosePullRequest(token, repo, number);
        }

        public async Task<DetailedUser> GetUser(InstallationToken token, string login)
        {
            return await _client.GetUser(token, login);
        }

        /// <summary>
        /// Returns the processing status of a pull request, would automatically set status to InProgress for unprocessed pulls
        /// </summary>
        /// <param name="token"></param>
        /// <param name="repo"></param>
        /// <param name="sha"></param>
        /// <returns></returns>
        public async Task<IPullStatus> GetPullStatus(InstallationToken token, string repo, IPullRequest pull)
        {
            //Check to see if there's a previously created CheckRun
            var checkSuites = await _client.GetCheckSuites(token, repo, pull.HeadSha);
            var checkSuite = checkSuites.FirstOrDefault(x => x.LatestCheckRunsCount > 0);
            CheckRun run = null;
            if (checkSuite != null)
            {
                var checkRuns = await _client.GetCheckRuns(token, repo, checkSuite.Id);
                run = checkRuns.FirstOrDefault(x => x.App.Id == _settings.ApplicationId);
            }

            if (run != null) return GetStatus(run, pull.Number);
            
            var checkRun = new CheckRun("P4 Submit", pull.HeadSha, CheckRun.RunStatus.InProgress)
            {
                StartedAt = DateTime.UtcNow
            };
            run = await _client.SubmitCheckRun(token, repo, checkRun);
            return GetStatus(run, pull.Number);
        }

        private static readonly Regex RegexRetry = new Regex(@"\[retry (\d+) out of \d+\]", RegexOptions.Compiled);
        private static int? GetRetries(string summary)
        {
            if (summary == null) return null;
            var match = RegexRetry.Match(summary);
            return match.Success ? (int?)int.Parse(match.Groups[1].Value) : null;
        }

        private static IPullStatus GetStatus(CheckRun run, long pullNumber)
        {
            SubmitStatus status;
            switch (run.Output?.Title)
            {
                case null:
                    status = SubmitStatus.InProgress;
                    break;
                case Messages.ShelveMsg:
                    status = SubmitStatus.Shelved;
                    break;
                case Messages.SubmitReadyMsg:
                    status = SubmitStatus.SubmitReady;
                    break;
                case Messages.SubmitMsg:
                    status = SubmitStatus.Submitted;
                    break;
                case Messages.MergeConflict:
                    status = SubmitStatus.MergeConflict;
                    break;
                case Messages.ReviewRequired:
                    status = SubmitStatus.ReviewRequired;
                    break;
                case Messages.ErrorMsg:
                    status = SubmitStatus.Error;
                    break;
                case Messages.SubmitRetry:
                    status = SubmitStatus.SubmitRetry;
                    break;
                default:
                    Logger.Info($"Unidentified submit status '{run.Output.Title}', assuming InProgress");
                    status = SubmitStatus.InProgress;
                    break;
            }

            return new PullStatus(run.Id, pullNumber, status, GetRetries(run.Output?.Summary));
        }
        
    }
}