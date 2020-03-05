﻿using System;
using System.Threading.Tasks;
using GitP4Sync.Models;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Queue;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace GitP4Sync.Repos
{
    internal class GithubActionsAzureRepo : IGithubActionsRepo<GithubAzureAction>
    {
        private readonly TimeSpan _coolingTime;

        private readonly CloudQueue _queue;
        private readonly CloudQueueClient _client;
        public GithubActionsAzureRepo(IOptions<GithubActionsSettings> options)
        {
            _coolingTime = TimeSpan.FromSeconds(options.Value.CoolingTime);
            var account = CloudStorageAccount.Parse(options.Value.QueueConnectionString);
            _client = account.CreateCloudQueueClient();
            _queue = _client.GetQueueReference(options.Value.QueueName);
        }

        /// <summary>
        /// Returns a single request, or null if no requests found
        /// </summary>
        /// <returns></returns>
        public async Task<GithubAzureAction> GetAction()
        {
            var message = await _queue.GetMessageAsync(_coolingTime, _client.DefaultRequestOptions, null);
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
            await _queue.UpdateMessageAsync(action.Message, TimeSpan.Zero , MessageUpdateFields.Visibility);
        }

    }




}