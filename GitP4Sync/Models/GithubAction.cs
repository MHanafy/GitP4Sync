
namespace GitP4Sync.Models
{

    public interface IGithubAction
    {
        long PullNumber { get; }
        string SenderLogin { get; }

    }
    public class GithubAction
    {
        public long PullNumber { get; }
        public string SenderLogin { get; }

        public GithubAction(long pullNumber, string senderLogin)
        {
            PullNumber = pullNumber;
            SenderLogin = senderLogin;
        }
    }

    public interface IKeyedGithubAction<out TKey> : IGithubAction
    {
        TKey Id { get; }
    }

    public class GithubActionBase<TKey> : GithubAction, IKeyedGithubAction<TKey>
    {
        public TKey Id { get; }
        public GithubActionBase(TKey id, long pullNumber, string senderLogin) : base(pullNumber, senderLogin)
        {
            Id = id;
        }
    }
}
