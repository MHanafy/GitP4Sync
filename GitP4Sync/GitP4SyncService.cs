using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GitP4Sync.Repos;
using MHanafy.GithubClient;
using MHanafy.GithubClient.Models;
using MHanafy.GithubClient.Models.Github;
using MHanafy.Scheduling;
using Microsoft.Extensions.Options;
using Action = MHanafy.GithubClient.Models.Github.Action;

namespace GitP4Sync.Services
{
    class GitP4SyncService
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly IScheduler _scheduler;
        private readonly GithubHttpClient _client;
        private readonly ScriptService _script;
        private readonly Settings _settings;
        private readonly GithubSettings _githubSettings;
        private readonly UserFileRepo _userRepo;
        private readonly IGithubActionsRepo<GithubAzureAction> _actionsRepo;

        public GitP4SyncService(IScheduler scheduler, GithubHttpClient client, ScriptService script,
            IOptions<Settings> settings, IOptions<GithubSettings> githubSettings, UserFileRepo userRepo, IGithubActionsRepo<GithubAzureAction> repo)
        {
            _scheduler = scheduler;
            _client = client;
            _script = script;
            _settings = settings.Value;
            _githubSettings = githubSettings.Value;
            _userRepo = userRepo;
            _actionsRepo = repo;
        }

        public async Task Start()
        {
            await _script.Init();
            await _script.Execute($"$Env:P4Client = '{_settings.P4Client}'");
            _scheduler.Start(async () => await Process());
        }

        public void Stop()
        {
            _scheduler.Stop();
        }

        private async Task<bool> Process()
        {
            try
            {
                var origin = await _script.Execute("GitGetRemote");
                var repo = (string) origin[0].Properties["Repository"].Value;
                var token = await _client.GetInstallationToken(_settings.GithubInstallationId);
                //Set the login for shell authentication
                await _script.Execute($"GitSetToken {token.Token}");

                var (hasChanges, needsSync) = await ProcessSubmitActions(token, repo);
                var (hasChanges2, needsSync2) = await ProcessPullRequests(token, repo);

                if (needsSync && needsSync2)
                {
                    hasChanges |= hasChanges2 || await Sync();
                }

                _userRepo.Save();

                return hasChanges;
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                return true;
            }
        }

        private const string ShelveMsg = "Changes were shelved to";
        private const string SubmitReadyMsg = "Ready to submit, Click submit to continue";
        private const string SubmitMsg = "Changes were submitted to Perforce";

        private async Task<(bool hasChanges, bool needsSync)> ProcessSubmitActions(InstallationToken token, string repo)
        {
            var didSync = false;
            var hasChanges = false;

            var action = await _actionsRepo.GetAction();
            while (action != null)
            {
                hasChanges = true;
                CheckRun checkRun = null;
                try
                {
                    if (action.CheckRun == null || !action.CheckRun.Output.Title.StartsWith(SubmitReadyMsg) ||
                        !long.TryParse(action.RequestedAction.Id, out var pullNumber))
                    {
                        Logger.Error(
                            $"Invalid submit request; pull '{action?.RequestedAction?.Id}' by '{action?.Sender?.Login}'");
                        await _actionsRepo.DeleteAction(action);
                        continue;
                    }

                    Logger.Info($"Started submitting pull {action.RequestedAction.Id} by {action.Sender.Login}");

                    var pull = await _client.GetPullRequest(token, repo, pullNumber);
                    checkRun = await GetCheckRun(token, repo, pull.Id, pull.Head.Sha, true);
                    var reviewerLogin = await ValidatePull(token, repo, pull, checkRun);
                    if (reviewerLogin == null)
                    {
                        await _actionsRepo.DeleteAction(action);
                        continue;
                    }
                    var (owner, reviewer) = await GetUsers(token, pull, checkRun, reviewerLogin);
                    if (owner == null || reviewer == null)
                    {
                        await _actionsRepo.DeleteAction(action);
                        continue;
                    }
                    await SubmitToPerforce(token, repo, pull, checkRun, owner, reviewer, true);
                    await _actionsRepo.DeleteAction(action);
                    didSync = true;
                }
                catch (Exception e)
                {
                    if (checkRun != null) await UpdateCheckRunError(e, token, repo, checkRun.Id);
                    Logger.Error(e);
                    //Always delete the request, because we'll telling the user about it and they can click to retry if desired.
                    await _actionsRepo.DeleteAction(action);
                }
                finally
                {
                    action = await _actionsRepo.GetAction();
                }
            }

            return (hasChanges, !didSync);
        }

        private async Task<(bool hasChanges, bool needsSync)> ProcessPullRequests(InstallationToken token, string repo)
        {
            var didSync = false;
            var hasChanges = false;
         
            var pulls = (await _client.GetPullRequests(token, repo))
                .Where(x => x.Base.Ref == "master" && x.State == PullRequest.PullStatus.Open);

            foreach (var pull in pulls)
            {
                Logger.Info($"Started processing pull {pull.Number} by {pull.User.Login}");
                var checkRun = await GetCheckRun(token, repo, pull.Id, pull.Head.Sha, false);
                if (checkRun == null) continue;
                hasChanges = true;
                try
                {
                    var pullDetails = await _client.GetPullRequest(token, repo, pull.Number);
                    var reviewerLogin = await ValidatePull(token, repo, pullDetails, checkRun);
                    var (owner, reviewer) = await GetUsers(token, pull, checkRun, reviewerLogin);
                    if (owner == null || reviewer == null) continue;

                    if (owner.AutoSubmit)
                    {
                        var action = new Action
                        {
                            Label = "Submit to Perforce", Identifier = pull.Number.ToString(),
                            Description = "Submit changes and close pull request"
                        };
                        await _client.UpdateCheckRun(token, repo, checkRun.Id, CheckRun.RunStatus.InProgress,
                            CheckRun.RunConclusion.ActionRequired,
                            new CheckRunOutput
                            {
                                Title = $"{SubmitReadyMsg}",
                                Summary = "Changes are ready to be submitted to Perforce, Click submit to continue."
                            }, DateTime.UtcNow, new List<Action> {action});
                        Logger.Info($"Pull {pull.Number} is ready to be submitted");
                    }
                    else
                    {
                        await SubmitToPerforce(token, repo, pull, checkRun, owner, reviewer, false);
                    }

                    didSync = true;
                }
                catch (Exception e)
                {
                    await UpdateCheckRunError(e, token, repo, checkRun.Id);
                    Logger.Error(e);
                }
            }

            return (hasChanges, !didSync);
        }

        private async Task SubmitToPerforce(InstallationToken token, string repo, PullRequest pull, CheckRun checkRun,  UserFileRepo.User owner, UserFileRepo.User reviewer, bool submit)
        {
            var pullTitle = $"{pull.Title} | Reviewed by {reviewer.P4Login}";
            var cmd = $"P4Submit commit {pull.Head.Sha} {owner.P4Login} '{pullTitle}' {(submit && _settings.AutoSubmitEnabled?'n':'y')}";
            var result = await _script.Execute(cmd);
            var changeList = result[0].BaseObject;
            if (submit && _settings.AutoSubmitEnabled)
            {
                checkRun.Conclusion = CheckRun.RunConclusion.Success;
                var summary = $"{SubmitMsg}; changelist '{changeList}'";
                await _client.UpdateCheckRun(token, repo, checkRun.Id, CheckRun.RunStatus.Completed,
                    CheckRun.RunConclusion.Success,
                    new CheckRunOutput {Title = SubmitMsg, Summary = summary});
                Logger.Info(summary);
            }
            else
            {
                checkRun.Conclusion = CheckRun.RunConclusion.ActionRequired;
                var summary = submit
                    ? $"AutoSubmit is disabled, {ShelveMsg} changelist '{changeList}' "
                    : $"{ShelveMsg} changelist '{changeList}'";
                await _client.UpdateCheckRun(token, repo, checkRun.Id, CheckRun.RunStatus.InProgress,
                    CheckRun.RunConclusion.ActionRequired,
                    new CheckRunOutput {Title = $"{ShelveMsg} '{changeList}'", Summary = summary});
                Logger.Info(summary);
            }
            await _client.ClosePullRequest(token, repo, pull.Number);
            Logger.Info($"Closed pull request {pull.Number}");
        }

        private async Task UpdateCheckRunError(Exception e, InstallationToken token, string repo, long checkRunId)
        {
            await _client.UpdateCheckRun(token, repo, checkRunId, CheckRun.RunStatus.InProgress,
                CheckRun.RunConclusion.ActionRequired,
                new CheckRunOutput {Title = $"Unexpected error: {e.GetType().Name}'", Summary = e.Message});
        }

        private async Task<string> ValidatePull(InstallationToken token, string repo, DetailedPullRequest pull,
            CheckRun checkRun)
        {

            if (pull.Mergeable == null)
            {
                Logger.Info(
                    $"Null mergable flag Skipping pull '{pull.Number}' - CheckRun status '{checkRun.Status}' conclusion '{checkRun.Conclusion}' ActionTitle '{checkRun.Output.Title}'");
                return null;
            }

            if (!pull.Mergeable.Value)
            {
                checkRun.Conclusion = CheckRun.RunConclusion.ActionRequired;
                var summary =
                    "Pull request can't be merged automatically, Please merge manually and update the branch to proceed";
                await _client.UpdateCheckRun(token, repo, checkRun.Id, CheckRun.RunStatus.InProgress,
                    CheckRun.RunConclusion.ActionRequired,
                    new CheckRunOutput
                        {Title = "Merge conflict - automatic merge not possible", Summary = summary});
                Logger.Info(summary);
                return null;
            }

            var review =
                (await _client.GetReviews(token, repo, pull.Number)).FirstOrDefault(x =>
                    x.State == Review.ReviewState.Approved);
            if (review == null)
            {
                checkRun.Conclusion = CheckRun.RunConclusion.ActionRequired;
                var summary = "An approved code review is required before changes can be submitted to Perforce";
                await _client.UpdateCheckRun(token, repo, checkRun.Id, CheckRun.RunStatus.InProgress,
                    CheckRun.RunConclusion.ActionRequired,
                    new CheckRunOutput
                        {Title = "Code review required", Summary = summary});
                Logger.Info(summary);
                return null;
            }

            return review.User.Login;
        }

        private async Task<(UserFileRepo.User Owner, UserFileRepo.User Reviewer)> GetUsers(InstallationToken token, PullRequest pull, CheckRun checkRun, string reviewerLogin)
        {
            var user = await GetUser(token, pull.User.Login);
            var reviewer = await GetUser(token, reviewerLogin);
            if (user != null && reviewer != null) return (user, reviewer);

            checkRun.Conclusion = CheckRun.RunConclusion.ActionRequired;
            var userStr = user == null ? $"'{pull.User.Login}'" : null;
            var reviewerStr = reviewer == null ? $"'{reviewerLogin}'" : null;
            var unmappedUsers =
                $"User{(userStr != null && reviewerStr != null ? "(s)" : null)} {userStr}{(userStr != null && reviewerStr != null ? " and " : null)}{reviewerStr}";
            var summary =
                $"Github {unmappedUsers} {(userStr!=null && reviewerStr!=null?"aren't":"isn't")} mapped and couldn't be mapped automatically; Please update users file to continue";
            await _client.UpdateCheckRun(token, pull.Head.Repo.Name, checkRun.Id, CheckRun.RunStatus.InProgress,
                CheckRun.RunConclusion.ActionRequired,
                new CheckRunOutput {Title = $"Unmapped {unmappedUsers}", Summary = summary});
            Logger.Info(summary);
            return (user, reviewer);

        }

        private async Task<CheckRun> GetCheckRun(InstallationToken token, string repo, long pull, string sha, bool submit)
        {
            //Check to see if there's a previously created CheckRun

            var checkSuites = await _client.GetCheckSuites(token, repo, sha);
            var checkSuite = checkSuites.FirstOrDefault(x => x.LatestCheckRunsCount > 0);
            if (checkSuite != null)
            {
                var checkRuns = await _client.GetCheckRuns(token, repo, checkSuite.Id);
                var run = checkRuns.FirstOrDefault(x => x.App.Id == _githubSettings.ApplicationId);
                if (run != null)
                {
                    if (run.Status == CheckRun.RunStatus.Completed &&
                        run.Conclusion == CheckRun.RunConclusion.Success ||
                        run.Conclusion == CheckRun.RunConclusion.ActionRequired && 
                        //Only return a null if not submitting to skip an open pull request
                        !submit &&
                        (run.Output.Title.StartsWith(ShelveMsg) || run.Output.Title.StartsWith(SubmitReadyMsg)))
                    {
                        Logger.Info(
                            $"Skipping pull Id '{pull}' - CheckRun status '{run.Status}' conclusion '{run.Conclusion}' ActionTitle '{run.Output.Title}'");
                        return null;
                    }

                    return run;
                }
            }

            var checkRun = new CheckRun("P4 Submit", sha, CheckRun.RunStatus.InProgress)
            {
                StartedAt = DateTime.UtcNow
            };
            return await _client.SubmitCheckRun(token, repo, checkRun);
        }

        private async Task<UserFileRepo.User> GetUser(InstallationToken token, string githubLogin)
        {
            var user = _userRepo.Get(githubLogin);
            if (user != null) return user;
            //Try identifying the user based on his name
            var githubUser = await _client.GetUser(token, githubLogin);
            if (githubUser.Name == null) return null;
            var p4User = githubUser.Name.Replace(" ", ".");
            var userExists = (bool) (await _script.Execute($"P4UserExists {p4User}"))[0].BaseObject;
            return userExists ? _userRepo.Add(githubLogin, p4User, githubUser.Name) : null;
        }

        private async Task<bool> Sync()
        {
            var upToDate = false;
            bool? hadChanges = null;
            while (!upToDate)
            {
                var result = await _script.Execute($"GitP4Sync {_settings.P4MaxChanges}");
                //this script function should always return a bool
                upToDate = (bool) result[0].Properties["UpToDate"].Value;
                //see if the first call had changes
                if (hadChanges == null) hadChanges = !upToDate;
            }
            return hadChanges.Value;
        }
        
    }
}

