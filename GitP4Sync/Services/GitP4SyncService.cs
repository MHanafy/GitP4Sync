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
using Microsoft.Azure.Storage.Queue;
using Microsoft.Extensions.Options;
using NLog;
using Action = MHanafy.GithubClient.Models.Github.Action;
using User = GitP4Sync.Models.User;

namespace GitP4Sync.Services
{


    public abstract class GitP4SyncService<T> : IGitP4SyncService<T>
    {
        protected abstract Logger Logger { get; }
        //private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly IScheduler _scheduler;
        private readonly IGithubClient _client;
        private readonly IScriptService _script;
        private readonly Settings _settings;
        private readonly GithubSettings _githubSettings;
        private readonly IUserRepo _userRepo;
        private readonly IGithubActionsRepo<IKeyedGithubAction<T>,T> _actionsRepo;

        private enum Stage
        {
            Validate,
            Submit
        }

        protected GitP4SyncService(IScheduler scheduler, IGithubClient client, IScriptService script,
            IOptions<Settings> settings, IOptions<GithubSettings> githubSettings, IUserRepo userRepo, IGithubActionsRepo<IKeyedGithubAction<T>,T> repo)
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
                var token = await _client.GetInstallationToken(_settings.GithubInstallationId);
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


        public async Task<(bool hasChanges, bool needsSync)> ProcessSubmitActions(InstallationToken token, string repo)
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

            var pull = await _client.GetPullRequest(token, repo, action.PullNumber);
            var checkRun = await GetCheckRun(token, repo, pull.Id, pull.Head.Sha, true);
            try
            {
                var reviewerLogin = await ValidatePull(token, repo, pull, checkRun);
                var (owner, reviewer) = await GetUsers(token, repo, pull, checkRun, reviewerLogin);
                if (owner == null || reviewer == null || pull.State != "open")
                {
                    //Ignore the request if conditions changed, or if the pull request was closed
                    return false;
                }

                //When processing a user submit request, always reset the retries counter to start over
                await SubmitToPerforce(token, repo, pull, checkRun, owner, reviewer, null);
                return true;
            }
            catch (Exception e)
            {
                await UpdateCheckRunError(e, token, repo, pull.Number, checkRun.Id, Stage.Submit);
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

        public async Task<(bool hasChanges, bool needsSync)> ProcessPullRequests(InstallationToken token, string repo)
        {
            var didSync = false;
            var hasChanges = false;
         
            var pulls = (await _client.GetPullRequests(token, repo))
                .Where(x => _settings.Branches.Contains(x.Base.Ref) && x.State == PullRequest.PullStatus.Open);

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
                    var (owner, reviewer) = await GetUsers(token, repo, pull, checkRun, reviewerLogin);
                    if (owner == null || reviewer == null) continue;

                    var retries = GetSubmitRetries(checkRun);
                    if (_actionsRepo.Enabled)
                    {
                        if (retries != null)
                        {
                            //retrying, submit instead of showing the submit button
                            await SubmitToPerforce(token, repo, pull, checkRun, owner, reviewer, retries);
                            didSync = true;
                        }
                        else
                        {
                            var actions = new List<Action> {GetSubmitAction(pull.Number)};
                            await _client.UpdateCheckRun(token, repo, checkRun.Id, CheckRun.RunStatus.InProgress,
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
                        await SubmitToPerforce(token, repo, pull, checkRun, owner, reviewer, retries);
                        didSync = true;
                    }
                }
                catch (Exception e)
                {
                    await UpdateCheckRunError(e, token, repo, pull.Number, checkRun.Id, Stage.Validate);
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
                var cmd = $"P4Submit commit {pull.Head.Sha} {pull.Base.Ref} {owner.P4Login} '{pullTitle}' {(userAutoSubmit && _settings.AutoSubmitEnabled?'n':'y')} {_settings.P4DeleteShelveDays}";
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

            return review?.User.Login;
        }

        private async Task<(User Owner, User Reviewer)> GetUsers(InstallationToken token, string repo, PullRequest pull, CheckRun checkRun, string reviewerLogin)
        {
            var user = await GetUser(token, pull.User.Login);
            var reviewer = reviewerLogin == null ? null : await GetUser(token, reviewerLogin);
            if (reviewerLogin == null)
            {
                if (user != null && !user.RequireCodeReview)
                {
                    Logger.Warn($"Skipping code review requirement for {user.GithubLogin} as configured in users.json");
                    reviewer = user;
                } else
                {            
                    checkRun.Conclusion = CheckRun.RunConclusion.ActionRequired;
                    var reviewRequired = "An approved code review is required before changes can be submitted to Perforce";
                    await _client.UpdateCheckRun(token, repo, checkRun.Id, CheckRun.RunStatus.InProgress,
                        CheckRun.RunConclusion.ActionRequired,
                        new CheckRunOutput
                            {Title = "Code review required", Summary = reviewRequired});
                    Logger.Info(reviewRequired);
                }
            }
            if (user != null && (reviewerLogin == null || reviewer != null)) return (user, reviewer);

            checkRun.Conclusion = CheckRun.RunConclusion.ActionRequired;
            var userStr = user == null ? $"'{pull.User.Login}'" : null;
            var reviewerStr = reviewerLogin !=null && reviewer == null ? $"'{reviewerLogin}'" : null;
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
                        (run.Output.Title.StartsWith(Messages.ShelveMsg) || run.Output.Title.StartsWith(Messages.SubmitReadyMsg)))
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

