﻿using System;
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
            Console.WriteLine("Starting ...");
            try
            {
                var rc = HostFactory.Run(x =>
                {
                    x.Service<IGitP4SyncAzureService>(
                        s =>
                        {
                            s.ConstructUsing(name =>
                            {
                                try
                                {
                                    var serviceProvider = SetupDi();
                                    return serviceProvider.GetService<IGitP4SyncAzureService>();
                                }
                                catch (Exception e)
                                {
                                    Logger.Error(e);
                                    Console.WriteLine($"DI Exception: {e}");
                                    throw;
                                }
                            });
                            s.WhenStarted(tc => {Console.WriteLine("Service started"); tc.Start().ConfigureAwait(false);});
                            s.WhenStopped(tc => tc.Stop());
                        }
                    );
                    x.RunAsLocalSystem();
                    x.SetDescription("GitP4Sync service, continuous Perforce <> Github code syncing");
                    x.SetDisplayName("GitP4Sync Service");
                    x.SetServiceName("GitP4SyncService");
                    x.EnableServiceRecovery(x=>x.RestartService(TimeSpan.FromMinutes(5)));
                    x.UseNLog();
                });

                var exitCode = (int) Convert.ChangeType(rc, rc.GetTypeCode());
                Environment.ExitCode = exitCode;            
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex}");
            }

            Console.WriteLine("Closing ..");
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
                .AddScoped<IGitP4SyncAzureService, GitP4SyncAzureService>()
                .AddScoped<IGithubService, GithubService>()
                .AddScoped<IGithubActionsAzureRepo, GithubActionsAzureRepo>()
                .BuildServiceProvider();

            return serviceProvider;
        }
    }
}
