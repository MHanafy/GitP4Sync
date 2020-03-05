using GitP4Sync.Models;
using MHanafy.GithubClient.Models.Github;
using Microsoft.Azure.Storage.Queue;
using User = MHanafy.GithubClient.Models.Github.User;

namespace GitP4Sync.Repos
{
    /// <inheritdoc />
    internal class GithubAzureAction : IGithubAction
    {
        public readonly CloudQueueMessage Message;

        public GithubAzureAction(CloudQueueMessage msg, GithubAction action)
        {
            Message = msg;
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