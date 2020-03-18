using MHanafy.GithubClient.Models.Github;
using Newtonsoft.Json;

namespace GitP4Sync.Models
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
        MHanafy.GithubClient.Models.Github.User Sender { get; }
        Installation Installation { get;  }
    }

    public class GithubAction : IGithubAction
    {
        public static class ActionName
        {
            public const string Created = "created";
            public const string Completed = "completed";
            public const string ReRequested = "rerequested";
            public const string Requested = "requested_action";
        }

        [JsonProperty("action")]
        public string Action { get; set; }

        [JsonProperty("requested_action")]
        public RequestedAction RequestedAction { get; set; }

        [JsonProperty("check_run")]
        public CheckRun CheckRun { get; set; }

        [JsonProperty("repository")]
        public Repository Repository { get; set; }

        [JsonProperty("sender")]
        public MHanafy.GithubClient.Models.Github.User Sender { get; set; }

        [JsonProperty("installation")]
        public Installation Installation { get; set; }
    }
}