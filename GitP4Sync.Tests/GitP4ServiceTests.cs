using System;
using System.Threading.Tasks;
using GitP4Sync.Models;
using GitP4Sync.Repos;
using GitP4Sync.Services;
using MHanafy.GithubClient;
using MHanafy.GithubClient.Models;
using MHanafy.GithubClient.Models.Github;
using MHanafy.Scheduling;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

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
        private IGithubActionsRepo<IGithubAzureAction> _actionsRepo;

        [TestInitialize]
        public void Init()
        {
            _scheduler = Substitute.For<IScheduler>();
            _client = Substitute.For<IGithubClient>();
            _scriptService = Substitute.For<IScriptService>();
            _settings = Substitute.For<IOptions<Settings>>();
            _githubSettings = Substitute.For<IOptions<GithubSettings>>();
            _userRepo = Substitute.For<IUserRepo>();
            _actionsRepo = Substitute.For<IGithubActionsRepo<IGithubAzureAction>>();
        }

        private IGithubAzureAction MockAction()
        {
            var checkRun = Substitute.For<CheckRun>();
            checkRun.Output.Returns(new Output() { Text = "", Title = "" });
            var action = Substitute.For<IGithubAzureAction>();
            action.CheckRun.Returns(checkRun);
            action.CheckRun.Output.Returns(new Output());
            action.CheckRun.Output.Title.Returns(GitP4SyncService.Messages.SubmitReadyMsg);
            action.Action.Returns(GithubAction.ActionName.Requested);
            action.RequestedAction.Id.Returns("10");
            return action;
        }


        [TestMethod]
        public async Task ProcessPullRequests_ActionsEnabledSubmitError_UpdatesRetryCount()
        {
            //Arrange
            var repo = "";
            var token = new InstallationToken(0,"","",DateTime.Now.AddHours(1));

            var service = new GitP4SyncService(_scheduler, _client, _scriptService, _settings, _githubSettings, _userRepo,
                _actionsRepo);
            var action = MockAction();

            //Act
            await service.ProcessAction(token, repo, action);

            //Assert
        }
    }
}
