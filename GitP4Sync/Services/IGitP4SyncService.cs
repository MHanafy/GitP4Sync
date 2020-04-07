using System.Threading.Tasks;
using GitP4Sync.Models;
using MHanafy.GithubClient.Models;

namespace GitP4Sync.Services
{
    public interface IGitP4SyncService<T>
    {
        Task Start();
        void Stop();
        Task<(bool hasChanges, bool needsSync)> ProcessSubmitActions(InstallationToken token, string repo);
        Task<bool> ProcessAction(InstallationToken token, string repo, IKeyedGithubAction<T> action);
        Task<(bool hasChanges, bool needsSync)> ProcessPullRequests(InstallationToken token, string repo);
        Task<bool> Sync();
    }
}