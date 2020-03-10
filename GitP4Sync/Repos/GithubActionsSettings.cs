namespace GitP4Sync.Repos
{
    internal class GithubActionsSettings
    {
        public bool Enabled { get; set; }
        public string QueueConnectionString { get; set; }
        public string QueueName { get; set; }
        public int CoolingTime { get; set; }
    }
}
