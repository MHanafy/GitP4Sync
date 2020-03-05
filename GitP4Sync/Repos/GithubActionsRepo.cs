using System;
using System.Threading.Tasks;
using MHanafy.GithubClient.Models.Github;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Queue;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

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


    internal class GithubActionsAzureRepo : IGithubActionsRepo<GithubAzureAction>
    {
        private readonly TimeSpan _coolingTime;


        private readonly CloudQueue _queue;
        public GithubActionsAzureRepo(IOptions<GithubActionsSettings> options)
        {
            _coolingTime = TimeSpan.FromSeconds(options.Value.CoolingTime);
            var account = CloudStorageAccount.Parse(options.Value.QueueConnectionString);
            var client = account.CreateCloudQueueClient();
            _queue = client.GetQueueReference(options.Value.QueueName);
        }

        /// <summary>
        /// Returns a single request, or null if no requests found
        /// </summary>
        /// <returns></returns>
        public async Task<GithubAzureAction> GetAction()
        {
            var message = await _queue.GetMessageAsync();
            if (message == null) return null;
            var action = JsonConvert.DeserializeObject<GithubAction>(message.AsString);
            return new GithubAzureAction(message, action);
        }

        /// <summary>
        /// Permanently Deletes an action
        /// </summary>
        /// <returns></returns>
        public async Task DeleteAction(GithubAzureAction action)
        {
            await _queue.DeleteMessageAsync(action.Message);
        }

        /// <summary>
        /// Saves the action back to the queue, so it shows up again after the default cooling period.
        /// </summary>
        /// <returns></returns>
        public async Task ReturnAction(GithubAzureAction action)
        {
            await _queue.UpdateMessageAsync(action.Message, _coolingTime, MessageUpdateFields.Visibility);
        }

    }




}
