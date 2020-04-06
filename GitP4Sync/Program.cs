using System;
using GitP4Sync.Models;
using GitP4Sync.Repos;
using GitP4Sync.Services;
using MHanafy.GithubClient;
using MHanafy.GithubClient.Models;
using MHanafy.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Topshelf;

namespace GitP4Sync
{
    class Program
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        static void Main()
        {
            var rc = HostFactory.Run(x =>
            {
                x.Service<GitP4SyncService>(
                    s =>
                    {
                        s.ConstructUsing(name =>
                        {
                            try
                            {
                                var serviceProvider = SetupDi();
                                return serviceProvider.GetService<GitP4SyncService>();
                            }
                            catch (Exception e)
                            {
                                Logger.Error(e);
                                throw;
                            }
                        });
                        s.WhenStarted(tc => tc.Start().ConfigureAwait(false));
                        s.WhenStopped(tc => tc.Stop());
                    }
                );
                x.RunAsLocalSystem();
                x.SetDescription("Git p4 Sync service, continuous P4 > Github code syncing");
                x.SetDisplayName("GitP4Service");
                x.SetServiceName("GitP4Service");
                x.UseNLog();
            });

            var exitCode = (int) Convert.ChangeType(rc, rc.GetTypeCode());
            Environment.ExitCode = exitCode;
        }

        private static IServiceProvider SetupDi()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("settings.json")
                .Build();
            
            var serviceProvider = new ServiceCollection()
                .AddOptions()
                .Configure<Settings>(config)
                .Configure<SchedulerSettings>(config.GetSection("SchedulerSettings"))
                .Configure<GithubSettings>(config.GetSection("GithubSettings"))
                .Configure<GithubActionsSettings>(config.GetSection("GithubActionsSettings"))
                .AddScoped<IJwtTokenFactory, GithubJwtTokenFactory>()
                .AddScoped<IGithubClient, GithubHttpClient>()
                .AddScoped<IUserRepo, UserFileRepo>()
                .AddScoped<IScheduler, Scheduler>()
                .AddScoped<IScriptService, ScriptService>()
                .AddScoped<GitP4SyncService>()
                .AddScoped<IGithubActionsRepo<IGithubAzureAction>, GithubActionsAzureRepo>()
                .BuildServiceProvider();

            return serviceProvider;
        }
    }
}
