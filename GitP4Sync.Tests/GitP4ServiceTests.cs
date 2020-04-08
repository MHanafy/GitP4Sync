using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GitP4Sync.Github;
using GitP4Sync.Models;
using GitP4Sync.Repos;
using GitP4Sync.Services;
using MHanafy.GithubClient;
using MHanafy.GithubClient.Models;
using MHanafy.GithubClient.Models.Github;
using MHanafy.Scheduling;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using Action = System.Action;
using User = GitP4Sync.Models.User;

namespace GitP4Sync.Tests
{
    [TestClass]
    public class GitP4ServiceTests
    {
        private IScheduler _scheduler;
        private IGithubClient _client;
        private IScriptService _scriptService;
        private IOptions<Settings> _settings;
        private IOptions<GithubSettings> _githubSettings;
        private IUserRepo _userRepo;
        private IGithubActionsRepo<IKeyedGithubAction<int>, int> _actionsRepo;

        [TestInitialize]
        public void Init()
        {
            _scheduler = Substitute.For<IScheduler>();
            _client = Substitute.For<IGithubClient>();
            _scriptService = Substitute.For<IScriptService>();
            _settings = Substitute.For<IOptions<Settings>>();
            var settings = Substitute.For<Settings>();
            settings.Retries = 3;
            settings.Branches = new HashSet<string>{"master"};
            _settings.Value.Returns(settings);
            _githubSettings = Substitute.For<IOptions<GithubSettings>>();
            _githubSettings.Value.Returns(Substitute.For<GithubSettings>());
            _userRepo = Substitute.For<IUserRepo>();
            _actionsRepo = Substitute.For<IGithubActionsRepo<IKeyedGithubAction<int>, int>>();
        }




        [TestMethod]
        public async Task ProcessAction_SubmitError_UpdatesRetryCount()
        {
            //Arrange
            var repo = "";
            var token = new InstallationToken(0,"","",DateTime.Now.AddHours(1));
            var pull = Substitute.For<DetailedPullRequest>();
            var head = Substitute.For<Base>();
            head.Sha = "";
            pull.Head = head;
            pull.User = new MHanafy.GithubClient.Models.Github.User {Login = ""};
            pull.State = "open";
            _client.GetPullRequest(Arg.Any<InstallationToken>(), repo, Arg.Any<long>()).Returns(pull);
            var checkSuite = Substitute.For<CheckSuite>();
            checkSuite.LatestCheckRunsCount = 1;
            checkSuite.Id = 1;
            var checkSuites = new List<CheckSuite> {checkSuite};
            _client.GetCheckSuites(Arg.Any<InstallationToken>(), repo, Arg.Any<string>())
                .Returns(checkSuites);
            var checkRun = new CheckRun("", "", "") {Output = new Output {Text = "", Title = Messages.SubmitReadyMsg}, App =  Substitute.For<App>()};
            _client.GetCheckRuns(Arg.Any<InstallationToken>(), repo, Arg.Any<long>())
                .Returns(new List<CheckRun> {checkRun});
            var user = Substitute.For<User>();
            user.RequireCodeReview = false;
            _userRepo.Get(Arg.Any<string>()).Returns(user);

            //var service = new GitP4SyncService<int>(_scheduler, _client, _scriptService, _settings, _githubSettings, _userRepo,_actionsRepo);
            //var action = MockAction();

            //Act
            //await service.ProcessAction(token, repo, action);

            //Assert
        }
    }
}
