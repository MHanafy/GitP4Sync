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
    public partial class GithubService : IGithubService
    {

        private readonly GithubSettings _settings;
        private readonly IGithubClient _client;
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public bool ForceSubmitEnabled { get; set; }
        public static class SubmitLabel
        {
            public const string Submit = "Submit to Perforce";
            public const string ForceSubmit = "Force Submit";
        }

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

        public async Task<(bool Valid, List<string> ReviewerLogins)> ValidatePull(InstallationToken token, string repo, IPullRequest pull, IPullStatus pullStatus, bool forceSubmit = false)
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

            //Update the check status if any check isn't completed or has failed
            if (!forceSubmit)
            {
                var failedChecks = pullStatus.Checks.Any(x => x.Value == false);
                if (failedChecks)
                {
                    await UpdatePullStatus(token, repo, pullStatus, SubmitStatus.FailedChecks);
                    return (false, null);
                }

                var pendingChecks = pullStatus.Checks.Any(x => x.Value == null);
                if (pendingChecks)
                {
                    await UpdatePullStatus(token, repo, pullStatus, SubmitStatus.PendingChecks);
                    return (false, null);
                }
            }

            var reviews = (await _client.GetReviews(token, repo, pull.Number));
            var reviewers = reviews
                .Where(x => x.State == Review.ReviewState.Approved)
                .Select(x=>x.User.Login)
                .ToList();

            return (true, reviewers);
        }

        public async Task UpdatePullStatus(InstallationToken token, string repo, long statusId,string[] unmappedUsers)
        {
            var title = Messages.UnmappedUsers;
            var users = unmappedUsers.Length == 1
                ? $"user '{unmappedUsers[0]}' isn't"
                : $"users '{string.Join("', '", unmappedUsers[..^1])}' and '{unmappedUsers[^1]}' aren't";
            var summary = $"Github {users} mapped and couldn't be mapped automatically; Please update users file to continue";
            var output = new CheckRunOutput { Title = title, Summary = summary };
            await UpdateCheckRun(token, repo, statusId, true, output);
        }

        public async Task UpdatePullStatus(InstallationToken token, string repo, long statusId, Exception ex)
        {
            var title = Messages.ErrorMsg;
            var summary = $"Operation failed\r\n{ex.Message}";
            var output = new CheckRunOutput { Title = title, Summary = summary };
            await UpdateCheckRun(token, repo, statusId, true, output);
        }

        public async Task UpdatePullStatus(InstallationToken token, string repo, IPullStatus status, Exception ex, bool showSubmit, int? retries, int maxRetries)
        {
            //When showing the submit button, update the title to let the user take action
            var title = showSubmit ? Messages.ErrorMsg : Messages.SubmitRetry;
            var retryStr = retries == null ? null : $"[retry {retries} out of {maxRetries}]";
            var submitMsg = showSubmit ? " click submit to retry again" : null;
            var summary = $"Operation failed {retryStr}{submitMsg}\r\n{ex.Message}";
            var actions = showSubmit?new List<Action> {GetSubmitAction(status.PullNumber)}:null;
            var output = new CheckRunOutput { Title = title, Summary = summary };
            await UpdateCheckRun(token, repo, status.Id, true, output, actions);
        }

        private async Task UpdateCheckRun(InstallationToken token, string repo, long checkRunId, bool actionRequired, CheckRunOutput output=null, List<Action> actions = null)
        {
            string conclusion = null;
            string status = CheckRun.RunStatus.InProgress;
            DateTime? completeDate = null;
            
            if (output != null)
            {
                conclusion = actionRequired ? CheckRun.RunConclusion.ActionRequired : CheckRun.RunConclusion.Success;
                status = CheckRun.RunStatus.Completed;
                completeDate = DateTime.UtcNow;
            }

            await _client.UpdateCheckRun(token, repo, checkRunId, status, conclusion, output, completeDate, actions);
            Logger.Info($"Status updated - {conclusion} {output?.Summary}");
        }

        private Action GetSubmitAction(long pullNumber)
        {
            return new Action
            {
                Label = SubmitLabel.Submit, Identifier = SubmitLabel.Submit,
                Description = "Submit changes and close pull request"
            };
        }

        private Action GetForceSubmitAction(long pullNumber)
        {
            return new Action
            {
                Label = SubmitLabel.ForceSubmit,
                Identifier = SubmitLabel.ForceSubmit,
                Description = "Ignore required checks and submit now"
            };
        }

        public async Task UpdatePullStatus(InstallationToken token, string repo, long statusId, string changeList, bool submitted, bool userAutoSubmit)
        {
            string title;
            string summary;
            if (submitted)
            {
                summary = $"{Messages.Submit}; changelist '{changeList}'";
                title = Messages.Submit;
            }
            else
            {
                summary = userAutoSubmit
                    ? $"AutoSubmit is disabled, {Messages.Shelve} changelist '{changeList}' "
                    : $"{Messages.Shelve} changelist '{changeList}', Contact administrator to enable AutoSubmit for your account";
                title = Messages.Shelve;
            }
            
            await UpdateCheckRun(token, repo, statusId, !submitted, new CheckRunOutput {Title = title, Summary = summary});
        }

        public async Task UpdatePullStatus(InstallationToken token, string repo, IPullStatus pullStatus, SubmitStatus submitStatus)
        {
            CheckRunOutput output;
            bool actionRequired = false;
            List<Action> actions = null;
            switch (submitStatus)
            {
                case SubmitStatus.MergeConflict:
                    output = new CheckRunOutput{Title = Messages.MergeConflict, Summary = Messages.MergeConflictSummary};
                    actionRequired = true;
                    break;
                case SubmitStatus.SubmitReady:
                    output = new CheckRunOutput{Title = Messages.SubmitReady, Summary = Messages.SubmitReadySummary};
                    actionRequired = true;
                    actions = new List<Action> {GetSubmitAction(pullStatus.PullNumber)};
                    break;
                case SubmitStatus.ReviewRequired:
                    output = new CheckRunOutput{Title = Messages.ReviewRequired, Summary = Messages.ReviewRequiredSummary};
                    actionRequired = true;
                    break;
                case SubmitStatus.PendingChecks:
                    var pendingChecks = string.Join(", ", pullStatus.Checks.Where(x => x.Value == null).Select(x=>x.Key));
                    output = new CheckRunOutput{Title = Messages.PendingChecks, Summary = $"{Messages.PendingChecksSummary}Pending check(s): {pendingChecks}"};
                    actions = GetForceSubmitActions(pullStatus.PullNumber);
                    actionRequired = true;
                    break;
                case SubmitStatus.FailedChecks:
                    var failedChecks = string.Join(", ", pullStatus.Checks.Where(x => x.Value == false).Select(x => x.Key));
                    output = new CheckRunOutput { Title = Messages.FailedChecks, Summary = $"{Messages.FailedChecksSummary}Failed check(s): {failedChecks}" };
                    actions = GetForceSubmitActions(pullStatus.PullNumber);
                    actionRequired = true;
                    break;
                case SubmitStatus.InProgress:
                    output = null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(submitStatus), submitStatus, "unexpected submit status");
            }
            await UpdateCheckRun(token, repo, pullStatus.Id, actionRequired, output, actions);
        }

        private List<Action> GetForceSubmitActions(long pullNumber)
        {
            if (!ForceSubmitEnabled) return null;
            return new List<Action> { GetForceSubmitAction(pullNumber) };
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
        public async Task<IPullStatus> GetPullStatus(InstallationToken token, string repo, IPullRequest pull, IList<string> checkNames)
        {
            var checkSuites = await _client.GetCheckSuites(token, repo, pull.HeadSha);
            var checks = checkNames.ToDictionary(x => x, y => (bool?) null);
            CheckRun run = null;
            foreach (var suite in checkSuites)
            {
                if (suite.LatestCheckRunsCount <= 0) continue; //skip suites that weren't run yet.
                if (suite.App.Id == _settings.ApplicationId)
                {
                    //Check to see if there's a previously created CheckRun
                    var checkRuns = await _client.GetCheckRuns(token, repo, suite.Id);
                    run = checkRuns.FirstOrDefault();
                    continue;
                }

                if (suite.Status == CheckRun.RunStatus.Completed && checks.ContainsKey(suite.App.Name))
                {
                    checks[suite.App.Name] = suite.Conclusion?.ToString() == CheckRun.RunConclusion.Success;
                }
            }

            if(run == null)
            {
                var checkRun = new CheckRun("P4 Submit", pull.HeadSha, CheckRun.RunStatus.InProgress)
                {
                    StartedAt = DateTime.UtcNow
                };
                run = await _client.SubmitCheckRun(token, repo, checkRun);
            }

            return GetStatus(run, pull.Number, checks);
           
        }

        private static readonly Regex RegexRetry = new Regex(@"\[retry (\d+) out of \d+\]", RegexOptions.Compiled);
        private static int? GetRetries(string summary)
        {
            if (summary == null) return null;
            var match = RegexRetry.Match(summary);
            return match.Success ? (int?)int.Parse(match.Groups[1].Value) : null;
        }

        private static IPullStatus GetStatus(CheckRun run, long pullNumber, IReadOnlyDictionary<string, bool?> checks)
        {
            SubmitStatus status;
            switch (run.Output?.Title)
            {
                case null:
                    status = SubmitStatus.InProgress;
                    break;
                case Messages.Shelve:
                    status = SubmitStatus.Shelved;
                    break;
                case Messages.SubmitReady:
                    status = SubmitStatus.SubmitReady;
                    break;
                case Messages.Submit:
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
                case Messages.UnmappedUsers:
                    status = SubmitStatus.UnmappedUsers;
                    break;
                case Messages.PendingChecks:
                    status = SubmitStatus.PendingChecks;
                    break;
                case Messages.FailedChecks:
                    status = SubmitStatus.FailedChecks;
                    break;
                default:
                    Logger.Info($"Unidentified submit status '{run.Output.Title}', assuming InProgress");
                    status = SubmitStatus.InProgress;
                    break;
            }

            return new PullStatus(run.Id, pullNumber, status, GetRetries(run.Output?.Summary), checks);
        }

        public async Task UpdatePullAction(InstallationToken token, string repo, IPullRequest pull, PullAction action)
        {
            await UpdateCheckRun(token, repo, pullStatus.Id, true, output, actions);

        }
    }
}