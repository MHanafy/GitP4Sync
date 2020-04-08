﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GitP4Sync.Models;
using MHanafy.GithubClient.Models;

namespace GitP4Sync.Services
{
    public interface IGithubService
    {
        Task<InstallationToken> GetToken(long installationId);
        Task<IPullRequest> GetPullRequest(InstallationToken token, string repo, long number);
        Task<IEnumerable<IPullRequest>> GetPullRequests(InstallationToken token, string repo);
        Task<(bool Valid, string ReviewerLogin)> ValidatePull(InstallationToken token, string repo, IPullRequest pull, IPullStatus pullStatus);
        Task UpdatePullStatus(InstallationToken token, string repo, long statusId,string[] unmappedUsers);
        Task UpdatePullStatus(InstallationToken token, string repo, long statusId, Exception ex);
        Task UpdatePullStatus(InstallationToken token, string repo, IPullStatus status, Exception ex, bool showSubmit, int? retries, int maxRetries);
        Task UpdatePullStatus(InstallationToken token, string repo, long statusId, string changeList, bool submitted, bool userAutoSubmit);
        Task UpdatePullStatus(InstallationToken token, string repo, IPullStatus status, SubmitStatus submitStatus);

        /// <summary>
        /// Returns the processing status of a pull request, would automatically set status to InProgress for unprocessed pulls
        /// </summary>
        /// <param name="token"></param>
        /// <param name="repo"></param>
        /// <param name="sha"></param>
        /// <returns></returns>
        Task<IPullStatus> GetPullStatus(InstallationToken token, string repo, IPullRequest pull);
    }
}