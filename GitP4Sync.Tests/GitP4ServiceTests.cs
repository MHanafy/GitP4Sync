using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GitP4Sync.Models;
using GitP4Sync.Repos;
using GitP4Sync.Services;
using MHanafy.GithubClient.Models;
using MHanafy.Scheduling;
using Microsoft.Azure.Storage.Queue;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using User = GitP4Sync.Models.User;

namespace GitP4Sync.Tests
{
    [TestClass]
    public class GitP4ServiceTests
    {
        private IScheduler _scheduler;
        private IScriptService _scriptService;
        private IOptions<Settings> _settings;
        private IUserRepo _userRepo;
        private IGithubActionsAzureRepo _actionsRepo;
        private IGithubService _githubService;
        private const string Repo = "repo";
        private InstallationToken _token;
        private User _user;

        [TestInitialize]
        public void Init()
        {
            _scheduler = Substitute.For<IScheduler>();
            _scriptService = Substitute.For<IScriptService>();
            _settings = Substitute.For<IOptions<Settings>>();
            var settings = Substitute.For<Settings>();
            settings.Branches = new HashSet<string>{"master"};
            _settings.Value.Returns(settings);
            _userRepo = Substitute.For<IUserRepo>();
            _actionsRepo = Substitute.For<IGithubActionsAzureRepo>();
            _githubService = Substitute.For<IGithubService>(); 
            _token = new InstallationToken(0,"","",DateTime.Now.AddHours(1));
            _user = Substitute.For<User>();
            _userRepo.Get(Arg.Any<string>()).Returns(_user);
        }

        [TestMethod]
        public async Task ProcessAction_SubmitError_UpdatesRetryCount()
        {
            //Arrange
            _user.RequireCodeReview = false;
            var action = Substitute.For<IKeyedGithubAction<CloudQueueMessage>>();
            _settings.Value.Retries = 2;
            var status = Substitute.For<IPullStatus>();
            status.Status.Returns(SubmitStatus.SubmitReady);
            var pull = Substitute.For<IPullRequest>();
            pull.Open.Returns(true);
            _githubService.GetPullStatus(_token, Repo, pull).Returns(status);
            _githubService.GetPullRequest(_token, Repo, Arg.Any<long>()).Returns(pull);
            _githubService.ValidatePull(_token, Repo, pull, status).Returns((true,null));
            var ex = new InvalidOperationException(Guid.NewGuid().ToString());
            _scriptService.Execute(Arg.Is<string>(x => x.StartsWith("P4Submit")), Arg.Any<bool>())
                .Throws(ex);
            var service = new GitP4SyncAzureService(_scheduler, _scriptService, _settings, _userRepo, _actionsRepo,
                _githubService);

            //Act
            await service.ProcessAction(_token, Repo, action);

            //Assert
            await _githubService.Received().UpdatePullStatus(_token, Repo, status, ex, false, 0, _settings.Value.Retries);
        }

        [TestMethod]
        [DataRow(0, 1)]
        [DataRow(1, 2)]
        [DataRow(2, 2)]
        public async Task ProcessPullRequest_SubmitError_UpdatesRetryCount(int? initialRetry, int? expectedRetry)
        {
            //Arrange
            _user.RequireCodeReview = false;
            _actionsRepo.Enabled.Returns(true);
            _settings.Value.Retries = 2;
            var status = Substitute.For<IPullStatus>();
            status.Retries.Returns(initialRetry);
            status.Status.Returns(initialRetry == null ? SubmitStatus.SubmitReady : SubmitStatus.SubmitRetry);
            var pull = Substitute.For<IPullRequest>();
            pull.Open.Returns(true);
            _githubService.GetPullStatus(_token, Repo, pull).Returns(status);
            _githubService.GetPullRequest(_token, Repo, Arg.Any<long>()).Returns(pull);
            _githubService.ValidatePull(_token, Repo, pull, status).Returns((true,null));
            var ex = new InvalidOperationException(Guid.NewGuid().ToString());
            _scriptService.Execute(Arg.Is<string>(x => x.StartsWith("P4Submit")), Arg.Any<bool>())
                .Throws(ex);
            var service = new GitP4SyncAzureService(_scheduler, _scriptService, _settings, _userRepo, _actionsRepo,
                _githubService);

            //Act
            await service.ProcessPullRequest(_token, Repo, pull);

            //Assert
            var showSubmit = initialRetry == _settings.Value.Retries - 1;
            if (initialRetry == expectedRetry)
            {
                await _githubService.DidNotReceiveWithAnyArgs().UpdatePullStatus(_token, Repo, status, ex, showSubmit,
                    expectedRetry, _settings.Value.Retries);
            }
            else
            {
                await _githubService.Received().UpdatePullStatus(_token, Repo, status, ex, showSubmit, expectedRetry,
                    _settings.Value.Retries);
            }
        }
    }
}
