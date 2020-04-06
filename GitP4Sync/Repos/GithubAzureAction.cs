using GitP4Sync.Models;
using MHanafy.GithubClient.Models.Github;
using Microsoft.Azure.Storage.Queue;
using User = MHanafy.GithubClient.Models.Github.User;

namespace GitP4Sync.Repos
{

    public interface IGithubAzureAction : IGithubAction
    {
        CloudQueueMessage Message { get; }
    }

    /// <inheritdoc />
    public class GithubAzureAction : IGithubAzureAction
    {
        public CloudQueueMessage Message => _message;
        private readonly CloudQueueMessage _message;

        public GithubAzureAction(CloudQueueMessage msg, GithubAction action)
        {
            _message = msg;
            Action = action.Action;
            CheckRun = action.CheckRun;
            Repository = action.Repository;
            Sender = action.Sender;
            Installation = action.Installation;
            RequestedAction = action.RequestedAction;
        }

        public string Action { get; }
        public RequestedAction RequestedAction { get; }
        public CheckRun CheckRun { get; }
        public Repository Repository { get; }
        public User Sender { get; }
        public Installation Installation { get; }

    }
}