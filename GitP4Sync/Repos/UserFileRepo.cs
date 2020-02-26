﻿using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace GitP4Sync.Repos
{
    class UserFileRepo
    {
        public class User
        {
            public string GithubLogin { get; set; }
            public string P4Login { get; set; }
            public string Name { get; set; }
        }

        private readonly FileSystemWatcher _watcher;
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public UserFileRepo()
        {
            _watcher = new FileSystemWatcher(".", FileName) {NotifyFilter = NotifyFilters.LastWrite};
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

        private const string FileName = "Users.Json";
        private readonly Dictionary<string, User> _users;

        public void Load()
        {
            if (!File.Exists(FileName)) return;
            lock (_users)
            {
                var users = JsonConvert.DeserializeObject<List<User>>(File.ReadAllText(FileName));
                _users.Clear();
                foreach (var user in users)
                {
                    if (!string.IsNullOrEmpty(user.GithubLogin) && !_users.ContainsKey(user.GithubLogin))
                    {
                        _users.Add(user.GithubLogin, user);
                    }
                }
            }
        }

        public User Get(string githubLogin)
        {
            lock (_users)
            {
                return _users.ContainsKey(githubLogin) ? _users[githubLogin] : null;
            }
        }

        private bool _hasChanges;
        public User Add(string githubLogin, string p4User, string name)
        {
            lock (_users)
            {
                if (_users.ContainsKey(githubLogin)) return _users[githubLogin];
                var user = new User {GithubLogin = githubLogin, P4Login = p4User, Name = name};
                _users.Add(githubLogin, user);
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
                File.WriteAllText(FileName, JsonConvert.SerializeObject(_users.Values));
                _watcher.EnableRaisingEvents = true;
                _hasChanges = false;
            }
        }
    }
}