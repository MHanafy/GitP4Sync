using System;
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
    }
}
