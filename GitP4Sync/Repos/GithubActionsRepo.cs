using System;
using System.Threading.Tasks;
using GitP4Sync.Models;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Queue;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace GitP4Sync.Repos
{
    public interface IGithubActionsAzureRepo : IGithubActionsRepo<IKeyedGithubAction<CloudQueueMessage>, CloudQueueMessage>{}
    public class GithubActionsAzureRepo : IGithubActionsAzureRepo
    {
        public bool Enabled { get; }
        private readonly TimeSpan _coolingTime;
        private readonly CloudQueue _queue;
        private readonly CloudQueueClient _client;
        private const string NotEnabled = "Github actions aren't enabled";
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public GithubActionsAzureRepo(IOptions<GithubActionsSettings> options)
        {
            Enabled = options.Value.Enabled;
            if (!Enabled) return;

            _coolingTime = TimeSpan.FromSeconds(options.Value.CoolingTime);
            var account = CloudStorageAccount.Parse(options.Value.QueueConnectionString);
            _client = account.CreateCloudQueueClient();
            _client.DefaultRequestOptions.MaximumExecutionTime = TimeSpan.FromSeconds(30);
            _client.DefaultRequestOptions.ServerTimeout = TimeSpan.FromSeconds(10);
            _queue = _client.GetQueueReference(options.Value.QueueName);
        }

        /// <summary>
        /// Returns a single request, or null if no requests found
        /// </summary>
        /// <returns></returns>
        public async Task<IKeyedGithubAction<CloudQueueMessage>> GetAction()
        {
            if(!Enabled) throw new InvalidOperationException(NotEnabled);
            while (true)
            {
                var message = await _queue.GetMessageAsync(_coolingTime, _client.DefaultRequestOptions, null);
                if (message == null) return null;
                var action = JsonConvert.DeserializeObject<Github.GithubAction>(message.AsString);
                if (action.Action != Github.GithubAction.ActionName.Requested)
                {
                    Logger.Info(
                        $"skipping submit request; action: '{action.Action}' pull '{action.RequestedAction?.Id}' by '{action.Sender?.Login}'");

                    await _queue.DeleteMessageAsync(message);
                    continue;
                }

                if (action.CheckRun == null || !long.TryParse(action.RequestedAction.Id, out var pullNumber))
                {
                    Logger.Error(
                        $"Invalid submit request; action: '{action.Action}' pull '{action.RequestedAction?.Id}' by '{action.Sender?.Login}'");
                    await _queue.DeleteMessageAsync(message);
                    continue;
                }
                return new GithubAzureAction(message, pullNumber, action.Sender.Login);
            }
        }

        /// <summary>
        /// Permanently Deletes an action
        /// </summary>
        /// <returns></returns>
        public async Task DeleteAction(CloudQueueMessage action)
        {
            if(!Enabled) throw new InvalidOperationException(NotEnabled);
            await _queue.DeleteMessageAsync(action);
        }

        /// <summary>
        /// Saves the action back to the queue, so it shows up again after the default cooling period.
        /// </summary>
        /// <returns></returns>
        public async Task ReturnAction(CloudQueueMessage action)
        {
            if(!Enabled) throw new InvalidOperationException(NotEnabled);
            await _queue.UpdateMessageAsync(action, TimeSpan.Zero , MessageUpdateFields.Visibility);
        }
    }
}
