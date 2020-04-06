using System.Threading.Tasks;
using GitP4Sync.Models;

namespace GitP4Sync.Repos
{

    public interface IGithubActionsRepo<T>
        where T:IGithubAction
    {
        /// <summary>
        /// Returns a single request, or null if no requests found
        /// </summary>
        /// <returns></returns>
        Task<T> GetAction();

        /// <summary>
        /// Permanently Deletes an action
        /// </summary>
        /// <returns></returns>
        Task DeleteAction(T action);

        /// <summary>
        /// Saves the action back to the queue, so it shows up again after the default cooling period.
        /// </summary>
        /// <returns></returns>
        Task ReturnAction(T action);
        /// <summary>
        /// A flag to indicate whether Actions are configured and enabled
        /// </summary>
        bool Enabled { get; }
    }
}