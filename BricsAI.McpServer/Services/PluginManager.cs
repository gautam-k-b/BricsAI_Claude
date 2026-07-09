using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BricsAI.Core;

namespace BricsAI.McpServer.Services
{
    public class PluginManager
    {
        private List<IToolPlugin> _plugins = new List<IToolPlugin>();

        public void LoadPlugins()
        {
            _plugins.Clear();
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            var pluginFiles = Directory.GetFiles(basePath, "BricsAI.Plugins*.dll", SearchOption.TopDirectoryOnly);

            foreach (var file in pluginFiles)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(file);
                    var types = assembly.GetTypes()
                                        .Where(t => typeof(IToolPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                    foreach (var type in types)
                    {
                        if (Activator.CreateInstance(type) is IToolPlugin plugin)
                        {
                            _plugins.Add(plugin);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggerService.LogTransaction("PLUGIN", $"Failed to load plugin {file}: {ex.Message}");
                }
            }
        }

        public IEnumerable<IToolPlugin> GetPluginsForVersion(int majorVersion)
        {
            return _plugins.Where(p => p.TargetVersion <= majorVersion);
        }

        public IToolPlugin? GetPluginForCommand(string netCommandName, int majorVersion)
        {
            return GetPluginsForVersion(majorVersion).FirstOrDefault(p => p.CanExecute(netCommandName));
        }
    }
}
