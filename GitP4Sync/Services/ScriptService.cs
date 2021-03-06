﻿using System;
using System.IO;
using System.Management.Automation;
using System.Threading.Tasks;
using GitP4Sync.Models;
using MHanafy.GithubClient;
using Microsoft.Extensions.Options;

namespace GitP4Sync.Services
{
    public class ScriptService : IScriptService
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private readonly PowerShell _shell;
        private readonly Settings _settings;

        public ScriptService(IOptions<Settings> settings)
        {
            _shell = PowerShell.Create();
            _shell.Streams.Error.DataAdded += Error_DataAdded;
            _shell.Streams.Information.DataAdded += Information_DataAdded;
            _shell.Streams.Warning.DataAdded += Warning_DataAdded;
            _settings = settings.Value;
        }

        public async Task Init()
        {
            await LoadScripts(_settings.Scripts);
            //Change to the working directory
            await Execute($"cd {_settings.WorkDir}");
            Clear();
        }

        private void Clear()
        {
            _shell.Commands.Clear();
            _shell.Streams.ClearStreams();
            _warnIndex = -1;
            _infoIndex = -1;
            _errIndex = -1;
        }

        private int _warnIndex=-1;
        private void Warning_DataAdded(object sender, DataAddedEventArgs e)
        {
            if (e.Index <= _warnIndex) return;
            Logger.Warn(_shell.Streams.Warning[e.Index]);
            _warnIndex = e.Index;
        }

        private int _infoIndex=-1;
        private void Information_DataAdded(object sender, DataAddedEventArgs e)
        {
            if (e.Index <= _infoIndex) return;
            Logger.Info(_shell.Streams.Information[e.Index]);
            _infoIndex = e.Index;
        }

        private int _errIndex=-1;
        private void Error_DataAdded(object sender, DataAddedEventArgs e)
        {
            if (e.Index <= _errIndex) return;
            //Many commands log to Error stream, even when there's no critical errors; hence making them Warning and leaving error to exceptions only
            Logger.Warn(_shell.Streams.Error[e.Index]);
            _errIndex = e.Index;
        }

        public async Task<PSDataCollection<PSObject>> Execute(string script, bool logResult = false, string msg = null)
        {
            try
            {
                Clear();
                Logger.Info("Executing " + (msg ?? script));
                _shell.AddScript(script);
                var result = await _shell.InvokeAsync();
                if (logResult)
                {
                    var output = result == null
                        ? "<Null>"
                        : $"result count = {result.Count} result:\r\n{string.Join("\r\n", result)}";
                    Logger.Info($"Script output: {output}");
                }
                return result;
            }
            catch (Exception e)
            {
                Logger.Error($"Error running {script}: {e.Message}");
                throw;
            }
            finally
            {
                Logger.Info($"Finished executing " + (msg ?? script));
            }
        }

        private async Task LoadScripts(string scripts)
        {
            try
            {
                foreach (var scriptName in scripts.Split(","))
                {
                    var file = PathHelper.GetFullPath($@"Scripts\{scriptName}");
                    var script = await File.ReadAllTextAsync(file);
                    _shell.AddScript(script).Invoke();
                    Logger.Info($"Loaded script '{file}'");
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
                throw;
            }
            finally
            {
                Logger.Info($"Finished loading scripts");
            }
        }
    }
}
