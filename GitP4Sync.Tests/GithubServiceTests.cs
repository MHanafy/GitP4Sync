using System;
using System.Collections.Generic;
using GitP4Sync.Models;
using GitP4Sync.Services;
using MHanafy.GithubClient;
using MHanafy.GithubClient.Models;
using MHanafy.GithubClient.Models.Github;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace GitP4Sync.Tests
{
    [TestClass]
    public class GithubServiceTests
    {
        [TestMethod]
        [DataRow(new [] {"1"}, "Github user '1' isn't mapped")]
        [DataRow(new [] {"1", "2"}, "Github users '1' and '2' aren't mapped")]
        [DataRow(new [] {"1", "2","3"}, "Github users '1', '2' and '3' aren't mapped")]
        [DataRow(new [] {"1", "2","3", "4"}, "Github users '1', '2', '3' and '4' aren't mapped")]
        public void UpdatePullStatus_UnmappedUsers_GeneratesCorrectMessage(string[] arr, string message)
        {
            //Arrange
            var repo = "repo";
            var statusId = 1;
            var client = Substitute.For<IGithubClient>();
            var service = new GithubService(client, Substitute.For<IOptions<GithubSettings>>());
            var token = new InstallationToken(0,"",repo,DateTime.Now.AddHours(1));
            
            //Act
            service.UpdatePullStatus(token, repo, statusId, arr).Wait();
            
            //Assert
            client.Received().UpdateCheckRun(token, repo, statusId, Arg.Any<string>(),
                CheckRun.RunConclusion.ActionRequired, Arg.Is<CheckRunOutput>(x => x.Summary.StartsWith(message)));
        }

        [TestMethod]
        public void GetPullStatus_SubmitRetry_ReturnsCorrectStatus()
        {
            //Arrange
            var repo = "repo";
            var token = new InstallationToken(0,"",repo,DateTime.Now.AddHours(1));
            var status = Substitute.For<IPullStatus>();
            var client = Substitute.For<IGithubClient>();
            CheckRunOutput output = null;
            client.UpdateCheckRun(token, repo, status.Id, CheckRun.RunStatus.Completed, Arg.Any<string>(), Arg.Do<CheckRunOutput>(x=> output = x), Arg.Any<DateTime?>(), null);
            var settings = Substitute.For<IOptions<GithubSettings>>();
            settings.Value.Returns(Substitute.For<GithubSettings>());
            var service = new GithubService(client, settings);
            service.UpdatePullStatus(token, repo, status, new Exception(), false, 0, 2).Wait();
            var suite = Substitute.For<CheckSuite>();
            suite.LatestCheckRunsCount = 1;
            suite.Id = 4;

            var run = new CheckRun("","","");
            run.App = new App();
            run.Output = Substitute.For<Output>();
            run.Output.Title = output.Title;
            run.Output.Summary = output.Summary;
            client.GetCheckSuites(token, repo, Arg.Any<string>()).Returns(new List<CheckSuite>{ suite} );
            client.GetCheckRuns(token, repo, suite.Id).Returns(new List<CheckRun>{run });

            //Act
            var result = service.GetPullStatus(token, repo, Substitute.For<IPullRequest>(), new List<string>()).Result;

            //Assert
            Assert.AreEqual(SubmitStatus.SubmitRetry, result.Status);
            Assert.AreEqual(0, result.Retries);
        }
    }
}
