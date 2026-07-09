using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BricsAI.Core;

namespace BricsAI.Overlay.Services
{
    public class PluginManager
    {
        private List<IToolPlugin> _plugins = new List<IToolPlugin>();

        public void LoadPlugins()
        {
            _plugins.Clear();
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            // The project reference copies the DLLs to the bin folder
            // So we can scan the BaseDirectory for any DLL starting with BricsAI.Plugins
            
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
                    Console.WriteLine($"Failed to load plugin {file}: {ex.Message}");
                }
            }
        }

        public IEnumerable<IToolPlugin> GetPluginsForVersion(int majorVersion)
        {
            // For example, if connected to V15, we only want tools where TargetVersion <= 15 (or exactly 15)
            // Or if connected to V19, we might want V19 tools, or V15 tools if no V19 specific tool exists.
            // For this design, let's keep it exact for simplicity, or return all tools where TargetVersion <= majorVersion
            return _plugins.Where(p => p.TargetVersion <= majorVersion);
        }

        public IToolPlugin? GetPluginForCommand(string netCommandName, int majorVersion)
        {
            // Iterates through loaded plugins specific to the version and finds the first one that claims it can handle the command
            return GetPluginsForVersion(majorVersion).FirstOrDefault(p => p.CanExecute(netCommandName));
        }
    }
}
