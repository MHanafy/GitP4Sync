using System;
using System.Collections.Generic;
using System.Text;

namespace GitP4Sync.Models
{
    public class PullRequest
    {
        public string Title { get; set; }
        public long Number { get; set; }
        public long Id { get; set; }
        public bool Open { get; set; }
        public string HeadSha { get; set; }
        public string BaseRef { get; set; }
        public string UserLogin { get; set; }
        public bool? Mergeable { get; set; }
    }
}
