# GitP4Sync

As with any software migration project, Source code migration takes time, requires change management, and can be quite risky.
GitP4Sync aims towards making this transition smooth, and greatly reduces the risk factor, all while getting the team onboard gradually.

Instead of having to do a big-bang (lift and shift) and having to answer all the questions before making the move, you can instead having the team gradually migrating to Github while keeping both Github and Perforce in perfect sync.
The benefets are:
- Better change management, onboarding people slowly
- Reduced risk, by trying what would the experience look like while still maintaing the old perforce repository.
- Early reaping the benefits of Github before making the complete move; i.e. start using checkin requirements, code reviews and building pipelines.
- Giving the developers the choice of using Git instead of Perforce.

This becomes possible because, instead of trying to make a 2-way sync and worrying about resolving conflicts; GitP4Sync utilizes a one way sync from Perforce to Git, while using Github pull requests as a proxy to capture Git changes and submit them to Perforce eliminating conflict resolution and enabling a smooth syncing mechanism that doesn't require manual intervention.

## prerequisites
- A git repository migrated from Perfoce using https://git-scm.com/docs/git-p4, and pushed to Github.com
- A worker machine to host the service with internet access, and network access to the Perforce server.
- A service account on the worker machine which has Git and Git-P4 configured, Perforce client and a workspace, and adequate storage for handling the repository size
- A private Github app with permission to `Checks`, `Contents`, and `Pull Requests`, installed on subject repository; create one here https://github.com/settings/apps
- All Gthub users should have mapped Perforce users.

After starting the service, it'll continuously pull the changes from Perforce to the Github repository.
Git developers will deal with the Github repository as any other repository, When the code is ready to submit, they should create a pull request.
Any pull request to master will be picked and processed by the Service, which will:
1. Check for conflicts, if any will report in the pull request
2. Check for an approved code review, and report if missing.
3. Try to map automatically map GitHub users to Perforce users based on First.Last names, if not will report in the pull request
4. If all checks passed, it goes and creates a changelist, shelving the files; and leaving the number in the pull request for the user to review and submit to perforce

In a future release, the service will automatically check-in to perforce, eliminating the need to deal with perforce.
A detailed step by step instructions in being developed, and will be published to this repo.