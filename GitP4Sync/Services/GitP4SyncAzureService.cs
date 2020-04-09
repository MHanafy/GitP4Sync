using GitP4Sync.Models;
using GitP4Sync.Repos;
using MHanafy.Scheduling;
using Microsoft.Azure.Storage.Queue;
using Microsoft.Extensions.Options;
using NLog;

namespace GitP4Sync.Services
{

    public interface IGitP4SyncAzureService : IGitP4SyncService<CloudQueueMessage>
    {
    }

    public class GitP4SyncAzureService : GitP4SyncService<CloudQueueMessage>, IGitP4SyncAzureService
    {
        public GitP4SyncAzureService(IScheduler scheduler, IScriptService script, IOptions<Settings> settings, IUserRepo userRepo, IGithubActionsAzureRepo repo,IGithubService githubService)
            : base(scheduler,script, settings, userRepo, repo, githubService)
        {
        }

        protected override Logger Logger => LogManager.GetCurrentClassLogger();
    }
}