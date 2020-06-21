# GitP4Sync

As with any software migration project, Source code migration takes time, requires change management, and can be quite risky.
GitP4Sync aims towards making this transition smooth, and greatly reduces the risk factor, all while getting the team onboard gradually.

Instead of having to do a big-bang (lift and shift) and having to answer all the questions before making the move, you can instead have the team gradually migrate to GitHub while keeping both GitHub and Perforce in perfect sync.
The benefits are:
- Better change management, onboarding people on their own pace.
- Reduced risk, by trying what would the experience look like while still maintaining the Perfoce repository as the source of truth.
- Early reaping the benefits of GitHub before making the complete move; i.e. start using check-in requirements, code reviews and building pipelines.
- Giving the developers the choice of using Git instead of Perforce.

This becomes possible because, instead of trying to make a 2-way sync and worrying about resolving conflicts; GitP4Sync utilizes a one way sync from Perforce to Git, while using GitHub pull requests as a proxy to capture Git changes and submit them to Perforce eliminating conflict resolution and enabling a smooth syncing mechanism that doesn't require manual intervention.

## Contents
- [GitP4Sync](#gitp4sync)
  - [Contents](#contents)
  - [How it works](#how-it-works)
  - [User triggered submit](#user-triggered-submit)
  - [Getting Started](#getting-started)
    - [A. Initial Repository Migration](#a-initial-repository-migration)
    - [B. Creating the GitHub App](#b-creating-the-github-app)
    - [C. Creating the Service Perforce User](#c-creating-the-service-perforce-user)
    - [D. Setting up the worker machine](#d-setting-up-the-worker-machine)
    - [E. Preparing the work folders](#e-preparing-the-work-folders)
    - [F. Installing the service](#f-installing-the-service)
    - [G. Service Configuration](#g-service-configuration)
    - [I. Logging configuration](#i-logging-configuration)
    - [J. Starting the service](#j-starting-the-service)
  - [Getting help](#getting-help)

## How it works
After starting the service, it'll continuously pull the changes from Perforce to the GitHub repository.
Git developers will deal with the GitHub repository as any other repository, When the code is ready to submit, they should create a pull request.

Any pull request to configured branches will be picked and processed by the Service, and will be either submitted or shelved depending on service and user configration.

The following checks are performed before a pull request can be submitted, and the status will be reflected in the Pull request.
1. Merge conflicts
2. Approved code review.
3. Mapped Perforce User, if automatic mapping is not applicable.

After successful completion, the pull request will be automatically closed, and the Perforce change list number will be listed.

![GitP4Sync workflow](/Images/GitP4Sync.png)

## User triggered submit
In the basic setup, the service will automatically submit pull requests as soon as they're ready.
A user triggered submit workflow can be enabled, which lets develoeprs push a button to submit.

To enable this, you'll need:
1. An Azure logic app (or similar) to provide a webhook to GitHub, and save the received message into an Azure Storage account.
2. Azure storage account to keep submit requests.

Whever the Submit button is clicked, GitHub will send a messge to the configured WebHook, which in turn saves the message in the Queue.
The service periodically checks the queue, and processes the submit requests.

![User triggered submit](/Images/UserTriggeredSubmit.png)

## Getting Started

### A. Initial Repository Migration
Before starting, you'll need to perform a manual migration using P4Sync.
The migration doesn't need to be full, a single commit is enough because the service will progressively bring the repository up to speed.

Important! Make sure to have branch protection rules to prevent developers from being able to commit (directly or via pull reuqestion); this is essential as all commits will be made to Perfoce by the service, and never to GitHub.

Follow this link for a step by step guide: [Perforce to Git Migration](https://www.linkedin.com/pulse/migrating-perforce-repository-git-mahmoud-hanafy/)

### B. Creating the GitHub App
The service uses GitHub Apps API to communicate with the GitHub repository, hence requires a free private app with proper permission.
To create the app:
1. Visit https://GitHub.com/settings/apps and select `New GitHub App`
2. Fill in the required field, Ensure to select `Read & Write` for `Checks`, `Contents`, and `Pull Requests` permissions; uncheck `Use Webhook` then save
3. You'll be redirected to the App page, click `Generate Private Key` and ensure to save the downloaded `PEM` file to a safe location as it'll be required later
4. Make note of the `App ID` found in the general section
5. Click `Install App` and follow the steps to install the App on the migrated repository
6. Make note of the InstallationId, which would show in the URL after installing the App, i.e. `https://GitHub.com/apps/gitp4sync/installations/12345` where `12345` is the InstallationId

### C. Creating the Service Perforce User
For the service to be able to sync and sumit, it needs a Perforce user; Ensure to make it a super user so the service can submit on behalf of password protected users.

### D. Setting up the worker machine
This is where the magic happens, we'll need a machine for the service to keep Perforce and GitHub in sync and process pull requests.

The machine specs will largery depend on the operating system, just ensure to have adequate storage and fast network/internet access to Perfoce.

 Since the service is using .Net Core, it can be compiled to a myriad of platforms including Linux; we've only tested it in production on Windows though.

1. Create a local user for the service; and login to it.
2. Setup Perforce client as you normally would, ensure to set `P4Port` environment variable.
3. Open P4V and login using the service Perforce user.
4. Create a workspace named `P4Submit` to be used for submitting; If you want to use a different name, make a note of the name to update the service settings.
5. Make a quick test to ensure you can fetch a file from the desired repository.
6. Ensure you can execute `P4` from the command line; if not, add your Perforce installation folder to `Path` environement variable.
7. Install `git-p4` following the steps in this guide [Perforce to Git Migration](https://www.linkedin.com/pulse/migrating-perforce-repository-git-mahmoud-hanafy/)
8. If you're planning to use `git LFS` ensure to set `git-p4` LFS settings correctly as explained in the guide above.

### E. Preparing the work folders
Now that we've a machine with both Perforce and GitHub access, we need to clone the repository.
1. Clone the migrated repository to a suitable folder, ensure there's adequate disk space for future activity. 
2. Ensure that these three refs are set, follow the last section in the guide on how to set them up. `refs/remotes/p4/master`, `refs/remotes/p4/HEAD`, `refs/remotes/origin/HEAD`
3. To ensure things are set correctly, open command prompt, and execute `git p4 sync --max-changes 1` under the cloned repository folder, verifying the command completes without errors.

### F. Installing the service
1. Download the latest stable release from [releases](https://GitHub.com/MHanafy/GitP4Sync/releases)
2. The service requires no installation, just extract the files under a suitable folder, i.e. `D:\P4Sync`
3. From a command prompt, change to the extracted files folder and execute the following command, replacing `%user%` and `%pass%` with the windows login details for the service user created before
    `GitP4Sync install -username %User% -password %pass%` 
4. If all went well, you should find the service listed in local services and set to login as the configured user.

### G. Service Configuration
Open `settings.json` and modify the required settings according to below table; settings are cold loaded hence you'll need to restart the service to pick the new settings.  
This is a list of the required settings, leave other settings to default values.

| Setting | Sample | Description |
|:---|:---|:---|
|WorkDir|D:\\\\GitRepository\\\\|The full path to the git repository|
|P4Client|GitSubmit|The name of the Perforce Workspace ***|
|P4User|GitP4Sync|The Perforce user name for the service|
|P4Pass|P@ssw0rd|Perforce service password|
|Scripts|Git.ps1,P4.ps1|Don't change this|
|GitHubInstallationId|12345|GitHub repository InstallationId *|
|Branches|master|a comma separated list of branches **|
|GitHubActionsSettings.Enabled|false|User triggered submit, keep as `false` or review the detailed setup steps for it|
|GitHubSettings.KeyPath|key.pem|location of the GitHub App Key, rename the file to match it and place it under the application folder *|
|GitHubSettings.ApplicationId|1234|The GitHub App ID *|

\* Review the [Creating the GitHub App](#b-creating-the-GitHub-app) section  
** Only migrated branches can be listed here.  
*** Review the [Setting up the worker machine](#d-setting-up-the-worker-machine) section 

### I. Logging configuration
For simplicity, the service uses a separate file for logging configuration; the default configuration keeps one log per day for 10 days.  
Modify `NLog.config` as suitable following [the NLog Guide](https://GitHub.com/nlog/nlog/wiki/Configuration-file)

### J. Starting the service
After all the hard work, time for the moment of truth :), to start the service, either execute `GitP4Sync Start` or head to windows services and start it from there.
I recommend opening `logs\log.txt` in any tail capable application like `NotePad++` to get familiar with the logs and ensure things are running well.

## Getting help
If you've any questions, look up the issues section, and if your question isn't answered create a new issue.