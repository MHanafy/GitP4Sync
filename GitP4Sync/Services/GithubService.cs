using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GitP4Sync.Models;
using MHanafy.GithubClient;
using MHanafy.GithubClient.Models;
using MHanafy.GithubClient.Models.Github;
using Microsoft.Extensions.Options;
using PullRequest = GitP4Sync.Models.PullRequest;

namespace GitP4Sync.Services
{
    public class GithubService
    {
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

        public async Task<PullRequest> GetPullRequest(InstallationToken token, string repo, long number)
        {
            var pull = await _client.GetPullRequest(token, repo, number);
            return MapPullRequest(pull);
        }

        public async Task<IEnumerable<PullRequest>> GetPullRequests(InstallationToken token, string repo)
        {
            return (await _client.GetPullRequests(token, repo)).Select(MapPullRequest);
        }

        public async Task<(bool Valid, string ReviewerLogin)> ValidatePull(InstallationToken token, string repo,
            PullRequest pull, PullStatus pullStatus)
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
                if(pullStatus.Status != SubmitStatus.MergeConflict) await UpdatePullStatus(token, repo, pullStatus.Id, SubmitStatus.MergeConflict);
                return (false, null);
            }

            var review =
                (await _client.GetReviews(token, repo, pull.Number)).FirstOrDefault(x =>
                    x.State == Review.ReviewState.Approved);

            return (true, review?.User.Login);
        }

        public async Task UpdatePullStatus(InstallationToken token, string repo, long statusId,
            string[] unmappedUsers)
        {
            var users = unmappedUsers.Length == 1
                ? $"user '{unmappedUsers[0]}' isn't"
                : $"users '{string.Join("', '", unmappedUsers[..^1])}' and '{unmappedUsers[^1]}' aren't";
            var summary =
                $"Github {users} mapped and couldn't be mapped automatically; Please update users file to continue";
            await _client.UpdateCheckRun(token, repo, statusId, CheckRun.RunStatus.Completed,
                CheckRun.RunConclusion.ActionRequired,
                new CheckRunOutput {Title = Messages.UnmappedUsers, Summary = summary});
        }

        public async Task UpdatePullStatus(InstallationToken token, string repo, long statusId, SubmitStatus status)
        {
            CheckRunOutput output = null;
            string conclusion = null;
            string runStatus = null;
            switch (status)
            {
                case SubmitStatus.MergeConflict:
                    output = new CheckRunOutput{Title = Messages.MergeConflict, Summary = Messages.MergeConflictSummary};
                    conclusion = CheckRun.RunConclusion.ActionRequired;
                    runStatus = CheckRun.RunStatus.Completed;
                    break;
                case SubmitStatus.Submitted:
                    break;
                case SubmitStatus.Shelved:
                    break;
                case SubmitStatus.SubmitReady:
                    break;
                case SubmitStatus.ReviewRequired:
                    output = new CheckRunOutput{Title = Messages.ReviewRequired, Summary = Messages.ReviewRequiredSummary};
                    conclusion = CheckRun.RunConclusion.ActionRequired;
                    runStatus = CheckRun.RunStatus.Completed;
                    break;
                case SubmitStatus.InProgress:
                    conclusion = null;
                    runStatus = CheckRun.RunStatus.InProgress;
                    break;
                case SubmitStatus.Error:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status), status, null);
            }
            await _client.UpdateCheckRun(token, repo, statusId, runStatus, conclusion, output);
            Logger.Info($"Status updated - {conclusion} {output?.Title}");
        }


        /// <summary>
        /// Returns the processing status of a pull request, would automatically set status to InProgress for unprocessed pulls
        /// </summary>
        /// <param name="token"></param>
        /// <param name="repo"></param>
        /// <param name="sha"></param>
        /// <returns></returns>
        public async Task<PullStatus> GetPullStatus(InstallationToken token, string repo, string sha)
        {
            //Check to see if there's a previously created CheckRun
            var checkSuites = await _client.GetCheckSuites(token, repo, sha);
            var checkSuite = checkSuites.FirstOrDefault(x => x.LatestCheckRunsCount > 0);
            CheckRun run = null;
            if (checkSuite != null)
            {
                var checkRuns = await _client.GetCheckRuns(token, repo, checkSuite.Id);
                run = checkRuns.FirstOrDefault(x => x.App.Id == _settings.ApplicationId);
            }

            if (run == null)
            {
                var checkRun = new CheckRun("P4 Submit", sha, CheckRun.RunStatus.InProgress)
                {
                    StartedAt = DateTime.UtcNow
                };
                run = await _client.SubmitCheckRun(token, repo, checkRun);
                return new PullStatus {Id = run.Id, Status = SubmitStatus.InProgress};
            }
            
            return new PullStatus {Id = run.Id, Status = GetStatus(run.Output?.Title)};
        }

        private SubmitStatus GetStatus(string outputTitle)
        {
            SubmitStatus status;
            if (outputTitle == null)
            {
                status = SubmitStatus.InProgress;
            }
            else
            {
                if (outputTitle.StartsWith(Messages.ShelveMsg))
                {
                    status = SubmitStatus.Shelved;
                }
                else if (outputTitle.StartsWith(Messages.SubmitReadyMsg))
                {
                    status = SubmitStatus.SubmitReady;
                }
                else if (outputTitle.StartsWith(Messages.SubmitMsg))
                {
                    status = SubmitStatus.Submitted;
                }
                else if (outputTitle == Messages.MergeConflict)
                {
                    status = SubmitStatus.MergeConflict;
                }
                else if (outputTitle == Messages.ReviewRequired)
                {
                    status = SubmitStatus.ReviewRequired;
                }
                else if (outputTitle.StartsWith(Messages.ErrorMsg))
                {
                    status = SubmitStatus.Error;
                }
                else
                {
                    Logger.Info($"Unidentified submit status '{outputTitle}', assuming InProgress ");
                    status = SubmitStatus.InProgress;
                }
            }

            return status;
        }
        
        private PullRequest MapPullRequest(MHanafy.GithubClient.Models.Github.PullRequest pull)
        {
            return new PullRequest
            {
                BaseRef = pull.Base.Ref, HeadSha = pull.Head.Sha, Number = pull.Number, Open = pull.State == "open",
                Title = pull.Title, Id = pull.Id, UserLogin = pull.User.Login
            };
        }

        private PullRequest MapPullRequest(DetailedPullRequest pull)
        {
            var result = MapPullRequest((MHanafy.GithubClient.Models.Github.PullRequest) pull);
            result.Mergeable = pull.Mergeable;
            return result;
        }
        
    }
}