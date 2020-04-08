using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GitP4Sync.Models;
using GitP4Sync.Repos;
using MHanafy.GithubClient;
using MHanafy.GithubClient.Models;
using MHanafy.GithubClient.Models.Github;
using MHanafy.Scheduling;
using Microsoft.Extensions.Options;
using NLog;
using Action = MHanafy.GithubClient.Models.Github.Action;
using PullRequest = GitP4Sync.Models.PullRequest;
using User = GitP4Sync.Models.User;

namespace GitP4Sync.Services
{
    public abstract class GitP4SyncService<T> : IGitP4SyncService<T>
    {
        protected abstract Logger Logger { get; }
        private readonly IScheduler _scheduler;
        private readonly IGithubClient _client;
        private readonly IScriptService _script;
        private readonly Settings _settings;
        private readonly GithubSettings _githubSettings;
        private readonly IUserRepo _userRepo;
        private readonly IGithubActionsRepo<IKeyedGithubAction<T>,T> _actionsRepo;
        private readonly GithubService _githubService;

        private enum Stage
        {
            Validate,
            Submit
        }

        protected GitP4SyncService(IScheduler scheduler, IGithubClient client, IScriptService script,
            IOptions<Settings> settings, IOptions<GithubSettings> githubSettings, IUserRepo userRepo, IGithubActionsRepo<IKeyedGithubAction<T>,T> repo, GithubService githubService)
        {
            _scheduler = scheduler;
            _client = client;
            _script = script;
            _settings = settings.Value;
            _githubSettings = githubSettings.Value;
            _userRepo = userRepo;
            _actionsRepo = repo;
            _githubService = githubService;
        }

        public async Task Start()
        {
            Logger.Info($"Service settings: {_settings}");
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
                var token = await _githubService.GetToken(_settings.GithubInstallationId);
                //Set the login for shell authentication
                await _script.Execute($"GitSetToken {token.Token}");

                var (hasChanges, needsSync) = await ProcessSubmitActions(token, repo);
                var (hasChanges2, needsSync2) = await ProcessPullRequests(token, repo);

                if (needsSync && needsSync2)
                {
                    hasChanges |= hasChanges2 | await Sync();
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


        private async Task<(bool hasChanges, bool needsSync)> ProcessSubmitActions(InstallationToken token, string repo)
        {
            if (!_actionsRepo.Enabled) return (false, true);
            var didSync = false;
            var hasChanges = false;

            var action = await _actionsRepo.GetAction();
            while (action != null)
            {
                hasChanges = true;
                try
                {
                    didSync = await ProcessAction(token,repo, action);
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                }
                finally
                {
                    action = await _actionsRepo.GetAction();
                }
            }

            return (hasChanges, !didSync);
        }

        public async Task<bool> ProcessAction(InstallationToken token, string repo, IKeyedGithubAction<T> action)
        {
            Logger.Info($"Started submitting pull {action.PullNumber} by {action.SenderLogin}");
           
            var pull = await _githubService.GetPullRequest(token, repo, action.PullNumber);
            if (!pull.Open) return false;
            var status = await _githubService.GetPullStatus(token, repo, pull.HeadSha);
            if (status.Status != SubmitStatus.SubmitReady) return false;

            try
            {
                var (valid, reviewerLogin) = await _githubService.ValidatePull(token, repo, pull, status);
                if (!valid) return false;
                var (owner, reviewer) = await GetUsers(token, repo, pull, status, reviewerLogin);
                if (owner == null || reviewer == null || !pull.Open)
                {
                    //Ignore the request if conditions changed, or if the pull request was closed
                    return false;
                }

                //When processing a user submit request, always reset the retries counter to start over
                //await SubmitToPerforce(token, repo, pull, status, owner, reviewer, null);
                return true;
            }
            catch (Exception e)
            {
                await UpdateCheckRunError(e, token, repo, pull.Number, status.Id, Stage.Submit);
                Logger.Error(e);
                return true;
            }
            finally
            {
                //Always delete the request, because we'll tell the user about it and they can click to retry if desired.
                await _actionsRepo.DeleteAction(action.Id);
            }
        }


        private readonly Regex _regexSubmitRetry =
            new Regex(@"Error during 'Submit' \[retry (\d+) out of \d+\]", RegexOptions.Compiled);
        private int? GetSubmitRetries(CheckRun checkRun)
        {
            var match = _regexSubmitRetry.Match(checkRun.Output.Summary);
            return match.Success ? (int?)int.Parse(match.Groups[1].Value) : null;
        }

        private async Task<(bool hasChanges, bool needsSync)> ProcessPullRequests(InstallationToken token, string repo)
        {
            var didSync = false;
            var hasChanges = false;
         
            var pulls = (await _githubService.GetPullRequests(token, repo))
                .Where(x => _settings.Branches.Contains(x.BaseRef) && x.Open);

            foreach (var pull in pulls)
            {
                Logger.Info($"Started processing pull {pull.Number} by {pull.UserLogin}");
                var status = await _githubService.GetPullStatus(token, repo, pull.HeadSha);
                if (!status.ActionRequired) continue;
                hasChanges = true;
                try
                {
                    //var pullDetails = await _client.GetPullRequest(token, repo, pull.Number);
                    var (valid, reviewerLogin) = await _githubService.ValidatePull(token, repo, pull, status);
                    if (!valid) continue;
                    var (owner, reviewer) = await GetUsers(token, repo, pull, status, reviewerLogin);
                    if (owner == null || reviewer == null) continue;

                    var retries = 1;// GetSubmitRetries(status);
                    if (_actionsRepo.Enabled)
                    {
                        if (retries != null)
                        {
                            //retrying, submit instead of showing the submit button
                            //await SubmitToPerforce(token, repo, pull, status, owner, reviewer, retries);
                            didSync = true;
                        }
                        else
                        {
                            var actions = new List<Action> {GetSubmitAction(pull.Number)};
                            await _client.UpdateCheckRun(token, repo, status.Id, CheckRun.RunStatus.InProgress,
                                CheckRun.RunConclusion.ActionRequired,
                                new CheckRunOutput
                                {
                                    Title = $"{Messages.SubmitReadyMsg}",
                                    Summary = "Changes are ready to be submitted to Perforce, Click submit to continue."
                                }, DateTime.UtcNow, actions);
                            Logger.Info($"Pull {pull.Number} is ready to be submitted");
                        }
                    }
                    else
                    {
                        //await SubmitToPerforce(token, repo, pull, status, owner, reviewer, retries);
                        didSync = true;
                    }
                }
                catch (Exception e)
                {
                    await UpdateCheckRunError(e, token, repo, pull.Number, status.Id, Stage.Validate);
                    Logger.Error(e);
                }
            }

            return (hasChanges, !didSync);
        }

        private async Task SubmitToPerforce(InstallationToken token, string repo, PullRequest pull, CheckRun checkRun,
            User owner, User reviewer, int? retries)
        {
            if (retries >= _settings.Retries) return;
            try
            {
                var pullTitle = $"{pull.Title} | Reviewed by {reviewer.P4Login}";
                var userAutoSubmit = owner.AutoSubmit ?? _settings.AutoSubmitDefault;
                var cmd = $"P4Submit commit {pull.HeadSha} {pull.BaseRef} {owner.P4Login} '{pullTitle}' {(userAutoSubmit && _settings.AutoSubmitEnabled?'n':'y')} {_settings.P4DeleteShelveDays}";
                var result = await _script.Execute(cmd);
                var changeList = result[0].BaseObject;
                if (userAutoSubmit && _settings.AutoSubmitEnabled)
                {
                    checkRun.Conclusion = CheckRun.RunConclusion.Success;
                    var summary = $"{Messages.SubmitMsg}; changelist '{changeList}'";
                    await _client.UpdateCheckRun(token, repo, checkRun.Id, CheckRun.RunStatus.Completed,
                        CheckRun.RunConclusion.Success,
                        new CheckRunOutput {Title = Messages.SubmitMsg, Summary = summary});
                    Logger.Info(summary);
                }
                else
                {
                    checkRun.Conclusion = CheckRun.RunConclusion.ActionRequired;
                    var summary = userAutoSubmit
                        ? $"AutoSubmit is disabled, {Messages.ShelveMsg} changelist '{changeList}' "
                        : $"{Messages.ShelveMsg} changelist '{changeList}', Contact administrator to enable AutoSubmit for your account";
                    await _client.UpdateCheckRun(token, repo, checkRun.Id, CheckRun.RunStatus.InProgress,
                        CheckRun.RunConclusion.ActionRequired,
                        new CheckRunOutput {Title = $"{Messages.ShelveMsg} '{changeList}'", Summary = summary});
                    Logger.Info(summary);
                }

                await _client.ClosePullRequest(token, repo, pull.Number);
                Logger.Info($"Closed pull request {pull.Number}");
            }
            catch (Exception e)
            {
                //First try would have null retries, when updating it should be 0, hence using -1
                var updatedRetries = (retries ?? -1) + 1;
                var showSubmit = _actionsRepo.Enabled && updatedRetries == _settings.Retries;
                await UpdateCheckRunError(e, token, repo, pull.Number, checkRun.Id, Stage.Submit, updatedRetries, showSubmit);
                Logger.Error(e);
            }
        }

        private Action GetSubmitAction(long pullNumber)
        {
            return new Action
            {
                Label = "Submit to Perforce", Identifier = pullNumber.ToString(),
                Description = "Submit changes and close pull request"
            };
        }

        private async Task UpdateCheckRunError(Exception e, InstallationToken token, string repo, long pullNumber,
            long checkRunId, Stage stage, int? retries = null, bool showSubmitButton = false)
        {
            var actions = showSubmitButton ? new List<Action> {GetSubmitAction(pullNumber)} : null;
            var retryStr = retries == null ? null : $"[retry {retries} out of {_settings.Retries}]";
            await _client.UpdateCheckRun(token, repo, checkRunId, CheckRun.RunStatus.InProgress,
                CheckRun.RunConclusion.ActionRequired,
                new CheckRunOutput
                {
                    Title = $"Unexpected {stage} error'", Summary = $"Error during '{stage}' {retryStr}\r\n{e.Message}"
                }, null, actions);
        }

        private async Task<(User Owner, User Reviewer)> GetUsers(InstallationToken token, string repo, PullRequest pull, PullStatus checkRun, string reviewerLogin)
        {
            var user = await GetUser(token, pull.UserLogin);
            var reviewer = reviewerLogin == null ? null : await GetUser(token, reviewerLogin);
            if (reviewerLogin == null)
            {
                if (user != null && !user.RequireCodeReview)
                {
                    Logger.Warn($"Skipping code review requirement for {user.GithubLogin} as configured in users.json");
                    reviewer = user;
                } else
                {
                    await _githubService.UpdatePullStatus(token, repo, checkRun.Id, SubmitStatus.ReviewRequired);
                }
            }
            if (user != null && (reviewerLogin == null || reviewer != null)) return (user, reviewer);

            //checkRun.Conclusion = CheckRun.RunConclusion.ActionRequired;
            var userStr = user == null ? $"'{pull.UserLogin}'" : null;
            var reviewerStr = reviewerLogin !=null && reviewer == null ? $"'{reviewerLogin}'" : null;
            var unmappedUsers =
                $"User{(userStr != null && reviewerStr != null ? "(s)" : null)} {userStr}{(userStr != null && reviewerStr != null ? " and " : null)}{reviewerStr}";
            var summary =
                $"Github {unmappedUsers} {(userStr!=null && reviewerStr!=null?"aren't":"isn't")} mapped and couldn't be mapped automatically; Please update users file to continue";
            await _client.UpdateCheckRun(token, repo, checkRun.Id, CheckRun.RunStatus.InProgress,
                CheckRun.RunConclusion.ActionRequired,
                new CheckRunOutput {Title = $"Unmapped {unmappedUsers}", Summary = summary});
            Logger.Info(summary);
            return (user, reviewer);

        }

        private async Task<User> GetUser(InstallationToken token, string githubLogin)
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

        public async Task<bool> Sync()
        {
            bool? hadChanges = null;
            try
            {
                foreach (var branch in _settings.Branches)
                {
                    var upToDate = false;
                    while (!upToDate)
                    {
                        var result = await _script.Execute($"GitP4Sync {branch} {_settings.P4MaxChanges}");
                        upToDate = (bool) result[0].Properties["UpToDate"].Value;
                        //see if the first call had changes
                        if (hadChanges == null) hadChanges = !upToDate;
                    }
                }
            }
            catch (Exception e)
            {
                //If an exception is caught no need to immediately retry to give a chance to recover, wait for the next run.
                Logger.Error(e);
                return true;
            }

            return hadChanges ?? false;
        }
        
    }
}

