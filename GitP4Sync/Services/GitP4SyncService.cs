using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GitP4Sync.Models;
using GitP4Sync.Repos;
using MHanafy.GithubClient.Models;
using MHanafy.Scheduling;
using Microsoft.Extensions.Options;
using NLog;

namespace GitP4Sync.Services
{
    public abstract class GitP4SyncService<T> : IGitP4SyncService<T>
    {
        protected abstract Logger Logger { get; }
        private readonly IScheduler _scheduler;
        private readonly IScriptService _script;
        private readonly Settings _settings;
        private readonly IUserRepo _userRepo;
        private readonly IGithubActionsRepo<IKeyedGithubAction<T>,T> _actionsRepo;
        private readonly IGithubService _githubService;

        protected GitP4SyncService(IScheduler scheduler, IScriptService script,
            IOptions<Settings> settings, IUserRepo userRepo, IGithubActionsRepo<IKeyedGithubAction<T>,T> repo, IGithubService githubService)
        {
            _scheduler = scheduler;
            _script = script;
            _settings = settings.Value;
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
                    didSync |= await ProcessAction(token,repo, action);
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
            if (!pull.Open)
            {
                Logger.Warn($"Ignoring a submit request for closed pull {pull.Number}");
                return false;
            }

            var status = await _githubService.GetPullStatus(token, repo, pull);
            if (status.Status != SubmitStatus.SubmitReady)
            {
                Logger.Warn($"Ignoring a submit request for pull {pull.Number} - status: {status}");
                return false;
            }

            try
            {
                var (valid, reviewerLogin) = await _githubService.ValidatePull(token, repo, pull, status);
                if (!valid) return false;
                var (owner, reviewer) = await GetUsers(token, repo, pull, status, reviewerLogin);
                if (owner == null || reviewer == null)
                {
                    //Ignore the request if review was revoked
                    Logger.Warn($"Ignoring a submit request for pull {pull.Number} as it no longer has an approved review");
                    return false;
                }

                //When processing a user submit request, always reset the retries counter to start over
                await SubmitToPerforce(token, repo, pull, status, owner, reviewer, null);
                return true;
            }
            catch (Exception e)
            {
                await UpdateSubmitError(token, repo, status, e);
                return false;
            }
            finally
            {
                //Always delete the request, because we'll tell the user about it and they can click to retry if desired.
                await _actionsRepo.DeleteAction(action.Id);
            }
        }

        private async Task UpdateSubmitError(InstallationToken token, string repo, IPullStatus status, Exception e)
        {
            //First try would have null retries, when updating it should be 0, hence using -1
            var updatedRetries = (status.Retries ?? -1) + 1;
            var showSubmit = _actionsRepo.Enabled && updatedRetries == _settings.Retries;
            await _githubService.UpdatePullStatus(token, repo, status, e, showSubmit, updatedRetries, _settings.Retries);
            Logger.Error(e);

        }

        private bool ActionRequired(IPullStatus status)
        {
            return status.Status == SubmitStatus.InProgress || status.Status == SubmitStatus.MergeConflict ||
                   status.Status == SubmitStatus.ReviewRequired || status.Status == SubmitStatus.UnmappedUsers ||
                   status.Status == SubmitStatus.SubmitRetry ||
                   status.Status == SubmitStatus.Error && status.Retries.GetValueOrDefault(0) < _settings.Retries;
        }

        private async Task<(bool hasChanges, bool needsSync)> ProcessPullRequests(InstallationToken token, string repo)
        {
            var didSync = false;
            var hasChanges = false;
         
            var pulls = (await _githubService.GetPullRequests(token, repo))
                .Where(x => _settings.Branches.Contains(x.BaseRef) && x.Open);

            foreach (var pull in pulls)
            {
                hasChanges = true;
                didSync |= await ProcessPullRequest(token, repo, pull);
            }

            return (hasChanges, !didSync);
        }

        public async Task<bool> ProcessPullRequest(InstallationToken token, string repo, IPullRequest pull)
        {
            Logger.Info($"Started processing pull {pull.Number} by {pull.UserLogin}");
            var status = await _githubService.GetPullStatus(token, repo, pull);
            if (!ActionRequired(status)) return false;
            try
            {
                var (valid, reviewerLogin) = await _githubService.ValidatePull(token, repo, pull, status);
                if (!valid) return false;
                var (owner, reviewer) = await GetUsers(token, repo, pull, status, reviewerLogin);
                if (owner == null || reviewer == null) return false;

                if (_actionsRepo.Enabled)
                {
                    if (status.Retries != null)
                    {
                        //retrying, submit instead of showing the submit button
                        await SubmitToPerforce(token, repo, pull, status, owner, reviewer, status.Retries);
                        return true;
                    }

                    await _githubService.UpdatePullStatus(token, repo, status, SubmitStatus.SubmitReady);
                    Logger.Info($"Pull {pull.Number} is ready to be submitted");
                    return false;
                }

                await SubmitToPerforce(token, repo, pull, status, owner, reviewer, status.Retries);
                return true;
            }
            catch (Exception e)
            {
                await _githubService.UpdatePullStatus(token, repo, status.Id, e);
                Logger.Error(e);
                return false;
            }
        }

        private async Task SubmitToPerforce(InstallationToken token, string repo, IPullRequest pull, IPullStatus status,
            User owner, User reviewer, int? retries)
        {
            if (retries >= _settings.Retries) return;
            try
            {
                var pullTitle = $"{pull.Title} | Reviewed by {reviewer.P4Login}";
                var userAutoSubmit = owner.AutoSubmit ?? _settings.AutoSubmitDefault;
                var cmd = $"P4Submit commit {pull.HeadSha} {pull.BaseRef} {owner.P4Login} '{pullTitle}' {(userAutoSubmit && _settings.AutoSubmitEnabled?'n':'y')} {_settings.P4DeleteShelveDays}";
                var result = await _script.Execute(cmd);
                var changeList = result[0].BaseObject.ToString();
                if (userAutoSubmit && _settings.AutoSubmitEnabled)
                {
                    await _githubService.UpdatePullStatus(token, repo, status.Id, changeList, true, false);
                }
                else
                {
                    await _githubService.UpdatePullStatus(token, repo, status.Id, changeList, false, userAutoSubmit);
                }

                await _githubService.ClosePullRequest(token, repo, pull.Number);
                Logger.Info($"Closed pull request {pull.Number}");
            }
            catch (Exception e)
            {
                await UpdateSubmitError(token, repo, status, e);
            }
        }

        private async Task<(User Owner, User Reviewer)> GetUsers(InstallationToken token, string repo, IPullRequest pull, IPullStatus status, string reviewerLogin)
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
                    await _githubService.UpdatePullStatus(token, repo, status, SubmitStatus.ReviewRequired);
                }
            }
            if (user != null && (reviewerLogin == null || reviewer != null)) return (user, reviewer);

            var unmappedUsers = new List<string>();
            if(user == null) unmappedUsers.Add(pull.UserLogin);
            if(reviewerLogin != null && reviewer == null) unmappedUsers.Add(reviewerLogin);
            await _githubService.UpdatePullStatus(token, repo, status.Id, unmappedUsers.ToArray());
            
            return (user, reviewer);
        }

        private async Task<User> GetUser(InstallationToken token, string githubLogin)
        {
            var user = _userRepo.Get(githubLogin);
            if (user != null) return user;
            //Try identifying the user based on his name
            var githubUser = await _githubService.GetUser(token, githubLogin);
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