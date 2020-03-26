
using System.Collections.Generic;

namespace GitP4Sync.Models
{
    class Settings
    {
        public string Scripts { get; set; }
        public string WorkDir { get; set; }
        public string P4Client { get; set; }
        public int P4MaxChanges { get; set; } = 10;
        public int P4DeleteShelveDays { get; set; } = 10;
        public long GithubInstallationId { get; set; }
        public HashSet<string> Branches { get; set; }
        public bool AutoSubmitEnabled { get; set; }
        public bool AutoSubmitDefault { get; set; } = true;

        public override string ToString() =>
            $"{nameof(P4MaxChanges)}={P4MaxChanges} {nameof(P4DeleteShelveDays)}={P4DeleteShelveDays} {nameof(AutoSubmitEnabled)}={AutoSubmitEnabled} {nameof(Branches)}: '{string.Join(',', Branches)}'";
    }
}
