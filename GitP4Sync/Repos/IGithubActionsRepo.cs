using System.Threading.Tasks;
using MHanafy.GithubClient.Models.Github;
using Newtonsoft.Json;

namespace GitP4Sync.Repos
{
    public class RequestedAction
    {
        [JsonProperty("identifier")]
        public string Id { get; set; }
    }
    public interface IGithubAction
    {
        string Action { get;  }
        RequestedAction RequestedAction { get; }
        CheckRun CheckRun { get;  }
        Repository Repository { get;  }
        User Sender { get; }
        Installation Installation { get;  }
    }

    public class GithubAction : IGithubAction
    {
        [JsonProperty("action")]
        public string Action { get; set; }

        [JsonProperty("requested_action")]
        public RequestedAction RequestedAction { get; set; }

        [JsonProperty("check_run")]
        public CheckRun CheckRun { get; set; }

        [JsonProperty("repository")]
        public Repository Repository { get; set; }

        [JsonProperty("sender")]
        public User Sender { get; set; }

        [JsonProperty("installation")]
        public Installation Installation { get; set; }
    }

    internal interface IGithubActionsRepo<T>
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
    }
}