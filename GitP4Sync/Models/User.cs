namespace GitP4Sync.Models
{
    internal class User
    {
        public string GithubLogin { get; set; }
        public string P4Login { get; set; }
        public string Name { get; set; }
        public bool AutoSubmit { get; set; }
        public bool RequireCodeReview { get; set; } = true;
    }
}