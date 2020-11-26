
namespace GitP4Sync.Models
{

    public interface IGithubAction
    {
        long PullNumber { get; }
        string SenderLogin { get; }
        bool ForceSubmit { get; }

    }
    public class GithubAction
    {
        public long PullNumber { get; }
        public string SenderLogin { get; }
        public bool ForceSubmit { get; }

        public GithubAction(long pullNumber, string senderLogin, bool forceSubmit)
        {
            PullNumber = pullNumber;
            SenderLogin = senderLogin;
            ForceSubmit = forceSubmit;
        }
    }

    public interface IKeyedGithubAction<out TKey> : IGithubAction
    {
        TKey Id { get; }
    }

    public class GithubActionBase<TKey> : GithubAction, IKeyedGithubAction<TKey>
    {
        public TKey Id { get; }
        public GithubActionBase(TKey id, long pullNumber, string senderLogin, bool forceSubmit) : base(pullNumber, senderLogin, forceSubmit)
        {
            Id = id;
        }
    }
}
