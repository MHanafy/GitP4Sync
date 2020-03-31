using Newtonsoft.Json;

namespace GitP4Sync.Models
{
    internal class User
    {
        public string GithubLogin { get; set; }
        public string P4Login { get; set; }
        public string Name { get; set; }
        [JsonProperty(NullValueHandling=NullValueHandling.Ignore)]
        public bool? AutoSubmit { get; set; }
        public bool RequireCodeReview { get; set; } = true;
    }
}