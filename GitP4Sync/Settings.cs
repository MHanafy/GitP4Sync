
namespace GitP4Sync
{
    class Settings
    {
        public string Scripts { get; set; }
        public string WorkDir { get; set; }
        public string P4Client { get; set; }
        public int P4MaxChanges { get; set; }
        public long GithubInstallationId { get; set; }
        public bool AutoSubmitEnabled { get; set; }
    }
}
