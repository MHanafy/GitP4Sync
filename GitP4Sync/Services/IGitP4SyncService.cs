using System.Threading.Tasks;
using GitP4Sync.Models;
using MHanafy.GithubClient.Models;

namespace GitP4Sync.Services
{
    public interface IGitP4SyncService<in T>
    {
        Task Start();
        void Stop();
        Task<bool> ProcessAction(InstallationToken token, string repo, IKeyedGithubAction<T> action);
        Task<bool> ProcessPullRequest(InstallationToken token, string repo, IPullRequest pull);
        Task<bool> Sync();
    }
}