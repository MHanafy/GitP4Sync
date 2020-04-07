using GitP4Sync.Models;
using Microsoft.Azure.Storage.Queue;

namespace GitP4Sync.Repos
{
    public class GithubAzureAction : GithubActionBase<CloudQueueMessage>
    {
        public GithubAzureAction(CloudQueueMessage id, long pullNumber, string senderLogin) : base(id, pullNumber,senderLogin)
        {
        }
    }
}