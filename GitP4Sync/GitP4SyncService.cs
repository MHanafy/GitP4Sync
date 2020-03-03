using System;
using System.Linq;
using System.Threading.Tasks;
using GitP4Sync.Repos;
using MHanafy.GithubClient;
using MHanafy.GithubClient.Models;
using MHanafy.GithubClient.Models.Github;
using MHanafy.Scheduling;
using Microsoft.Extensions.Options;


namespace GitP4Sync
{
    class GitP4SyncService
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly IScheduler _scheduler;
        private readonly GithubHttpClient _client;
        private readonly Script _script;
        private readonly Settings _settings;
        private readonly GithubSettings _githubSettings;
        private readonly UserFileRepo _userRepo;

        public GitP4SyncService(IScheduler scheduler, GithubHttpClient client, Script script,
            IOptions<Settings> settings, IOptions<GithubSettings> githubSettings, UserFileRepo userRepo)
        {
            _scheduler = scheduler;
            _client = client;
            _script = script;
            _settings = settings.Value;
            _githubSettings = githubSettings.Value;
            _userRepo = userRepo;
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
                var (hasChanges, needsSync) = await ProcessPullRequests();
                if (needsSync)
                {
                    hasChanges |= await Sync();
                }

                return hasChanges;
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                return true;
            }
        }

        private const string Shelve = "Changes were shelved to";

        private async Task<(bool hasChanges, bool needsSync)> ProcessPullRequests()
        {
            var didSync = false;
            var hasChanges = false;
            var origin = await _script.Execute("GitGetRemote");
            //var owner = (string) origin[0].Properties["Owner"].Value;
            var repo = (string) origin[0].Properties["Repository"].Value;

            var token = await _client.GetInstallationToken(_settings.GithubInstallationId);
            var pulls = (await _client.GetPullRequests(token, repo))
                .Where(x => x.Base.Ref == "master" && x.State == PullRequest.PullStatus.Open);

            //Set the login for shell authentication
            await _script.Execute($"GitSetToken {token.Token}");

            foreach (var pull in pulls)
            {
                Logger.Info($"Started processing pull {pull.Number} by {pull.User.Login}");
                var checkRun = await GetCheckRun(token, repo, pull.Id, pull.Head.Sha);
                if (checkRun == null) continue;
                hasChanges = true;
                try
                {
                    var pullDetails = await _client.GetPullRequest(token, repo, pull.Number);

                    if (pullDetails.Mergeable==null)
                    {
                        Logger.Info(
                            $"Null mergable flag Skipping pull '{pull.Number}' - CheckRun status '{checkRun.Status}' conclusion '{checkRun.Conclusion}' ActionTitle '{checkRun.Output.Title}'");
                        continue;
                    }

                    if (!pullDetails.Mergeable.Value)
                    {
                        checkRun.Conclusion = CheckRun.RunConclusion.ActionRequired;
                        var summary = "Pull request can't be merged automatically, Please merge manually and update the branch to proceed";
                        await _client.UpdateCheckRun(token, repo, checkRun.Id, CheckRun.RunStatus.InProgress,
                            CheckRun.RunConclusion.ActionRequired,
                            new CheckRunOutput
                                {Title = "Merge conflict - automatic merge not possible", Summary = summary});
                        Logger.Info(summary);
                        continue;
                    }

                    var review = (await _client.GetReviews(token, repo, pull.Number)).FirstOrDefault(x=>x.State == Review.ReviewState.Approved);
                    if (review == null)
                    {
                        checkRun.Conclusion = CheckRun.RunConclusion.ActionRequired;
                        var summary = "An approved code review is required before changes can be submitted to Perforce";
                        await _client.UpdateCheckRun(token, repo, checkRun.Id, CheckRun.RunStatus.InProgress,
                            CheckRun.RunConclusion.ActionRequired,
                            new CheckRunOutput
                                {Title = "Code review required", Summary = summary});
                        Logger.Info(summary);
                        continue;
                    }

                    var users = await GetUsers(token, pull, checkRun, review);
                    if (users.Owner == null || users.Reviewer == null) continue;

                    var pullTitle = $"{pull.Title} | Reviewed by {users.Reviewer.P4Login}";
                    var cmd = $"P4Submit commit {pull.Head.Sha} {users.Owner.P4Login} '{pullTitle}'";
                    var result =await _script.Execute(cmd);
                    var changeList = result[0].BaseObject;
                    checkRun.Conclusion = CheckRun.RunConclusion.ActionRequired;
                    var shelveSummary = $"Changes were successfully shelved to changelist '{changeList}'";
                    await _client.UpdateCheckRun(token, repo, checkRun.Id, CheckRun.RunStatus.InProgress,
                        CheckRun.RunConclusion.ActionRequired,
                        new CheckRunOutput {Title = $"{Shelve} '{changeList}'", Summary = shelveSummary});
                    Logger.Info(shelveSummary);
                    await _client.ClosePullRequest(token, repo, pull.Number);
                    Logger.Info($"Closed pull request {pull.Number}");
                    didSync = true;
                }
                catch (Exception e)
                {
                    await _client.UpdateCheckRun(token, repo, checkRun.Id, CheckRun.RunStatus.InProgress,
                        CheckRun.RunConclusion.ActionRequired,
                        new CheckRunOutput {Title = $"Unexpected error: {e.GetType().Name}'", Summary = e.Message});
                    throw;
                }
            }

            _userRepo.Save();
            return (hasChanges, !didSync);
        }

        private async Task<(UserFileRepo.User Owner, UserFileRepo.User Reviewer)> GetUsers(InstallationToken token, PullRequest pull, CheckRun checkRun, Review review)
        {
            var user = await GetUser(token, pull.User.Login);
            var reviewer = await GetUser(token, review.User.Login);
            if (user != null && reviewer != null) return (user, reviewer);

            checkRun.Conclusion = CheckRun.RunConclusion.ActionRequired;
            var userStr = user == null ? $"'{pull.User.Login}'" : null;
            var reviewerStr = reviewer == null ? $"'{review.User.Login}'" : null;
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

        private async Task<CheckRun> GetCheckRun(InstallationToken token, string repo, long pull, string sha)
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
                        run.Output.Title.StartsWith(Shelve))
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

