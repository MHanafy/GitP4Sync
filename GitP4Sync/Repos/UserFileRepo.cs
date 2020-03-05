using System.Collections.Generic;
using System.IO;
using GitP4Sync.Models;
using MHanafy.GithubClient;
using Newtonsoft.Json;

namespace GitP4Sync.Repos
{
    class UserFileRepo
    {
        private readonly FileSystemWatcher _watcher;
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public UserFileRepo()
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(_fileName), Path.GetFileName(_fileName)) {NotifyFilter = NotifyFilters.LastWrite};
            _watcher.Changed += _watcher_Changed;
            _watcher.EnableRaisingEvents = true;
            _users = new Dictionary<string, User>();
            Load();
        }

        private void _watcher_Changed(object sender, FileSystemEventArgs e)
        {
            Logger.Info("User.Json was updated, reloading users");
            Load();
        }

        private readonly string _fileName = PathHelper.GetFullPath("Users.Json");
        private readonly Dictionary<string, User> _users;

        public void Load()
        {
            if (!File.Exists(_fileName))
            {
                Logger.Info($"No users loaded, file '{_fileName}' doesn't exist");
                return;
            }
            lock (_users)
            {
                var users = JsonConvert.DeserializeObject<List<User>>(File.ReadAllText(_fileName));
                _users.Clear();
                foreach (var user in users)
                {
                    var login = user.GithubLogin?.ToLowerInvariant();
                    if (!string.IsNullOrEmpty(login) && !_users.ContainsKey(login))
                    {
                        _users.Add(login, user);
                    }
                    else
                    {
                        Logger.Warn($"Invalid or duplicate user '{user.GithubLogin}'");
                    }

                    if (user.AutoSubmit)
                    {
                        Logger.Warn($"User: {user.GithubLogin} is set to {nameof(User.AutoSubmit)}");
                    }
                }
                Logger.Info($"Loaded {_users.Count} users");
            }
        }

        public User Get(string githubLogin)
        {
            var login = githubLogin.ToLowerInvariant();
            lock (_users)
            {
                return _users.ContainsKey(login) ? _users[login] : null;
            }
        }

        private bool _hasChanges;
        public User Add(string githubLogin, string p4User, string name)
        {
            var login = githubLogin.ToLowerInvariant();
            lock (_users)
            {
                if (_users.ContainsKey(login)) return _users[login];
                var user = new User {GithubLogin = githubLogin, P4Login = p4User, Name = name};
                _users.Add(login, user);
                _hasChanges = true;
                return user;
            }
        }

        public void Save()
        {
            lock (_users)
            {
                if (!_hasChanges) return;
                _watcher.EnableRaisingEvents = false;
                File.WriteAllText(_fileName, JsonConvert.SerializeObject(_users.Values));
                _watcher.EnableRaisingEvents = true;
                _hasChanges = false;
            }
        }
    }
}
