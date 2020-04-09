namespace GitP4Sync.Repos
{
    public class GithubActionsSettings
    {
        public bool Enabled { get; set; }
        public string QueueConnectionString { get; set; }
        public string QueueName { get; set; }
        public int CoolingTime { get; set; }
    }
}
