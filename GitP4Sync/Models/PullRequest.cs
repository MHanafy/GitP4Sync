namespace GitP4Sync.Models
{
    public interface IPullRequest
    {
        long Id { get; }
        string Title { get; }
        long Number { get; }
        bool Open { get; }
        string HeadSha { get; }
        string BaseRef { get; }
        string HeadRef { get; }
        string UserLogin { get; }
        bool? Mergeable { get; set; }
    }

    public class PullRequest : IPullRequest
    {
        public string Title { get; private set; }
        public long Number { get;  private set;}
        public long Id { get;  private set;}
        public bool Open { get;  private set;}
        public string HeadSha { get;  private set;}
        public string BaseRef { get;  private set;}
        public string HeadRef { get; private set; }
        public string UserLogin { get;  private set;}
        public bool? Mergeable { get; set; }

        public static PullRequest CreateFrom(MHanafy.GithubClient.Models.Github.PullRequest pull)
        {
            return new PullRequest
            {
                BaseRef = pull.Base.Ref, HeadSha = pull.Head.Sha, Number = pull.Number, Open = pull.State == "open",
                Title = pull.Title, Id = pull.Id, UserLogin = pull.User.Login, HeadRef = pull.Head.Ref
            };
        }

        public static PullRequest CreateFrom(MHanafy.GithubClient.Models.Github.DetailedPullRequest pull)
        {
            var result = CreateFrom((MHanafy.GithubClient.Models.Github.PullRequest) pull);
            result.Mergeable = pull.Mergeable;
            return result;
        }
    }
}
