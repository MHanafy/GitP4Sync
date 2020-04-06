using GitP4Sync.Models;

namespace GitP4Sync.Repos
{
    public interface IUserRepo
    {
        void Load();
        User Get(string githubLogin);
        User Add(string githubLogin, string p4User, string name);
        void Save();
    }
}