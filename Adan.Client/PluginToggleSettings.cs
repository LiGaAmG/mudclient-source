namespace Adan.Client
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    using Common.Utils;

    internal sealed class PluginToggleSettings
    {
        private const string ConfigFileName = "plugin-toggles.conf";
        private readonly Dictionary<string, bool> _pluginStates;

        private PluginToggleSettings(Dictionary<string, bool> pluginStates)
        {
            _pluginStates = pluginStates;
        }

        public static PluginToggleSettings Load()
        {
            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
                if (!File.Exists(configPath))
                {
                    return new PluginToggleSettings(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
                }

                var pluginStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (var rawLine in File.ReadAllLines(configPath))
                {
                    var line = rawLine == null ? string.Empty : rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    {
                        continue;
                    }

                    var separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
                    {
                        continue;
                    }

                    var pluginName = line.Substring(0, separatorIndex).Trim();
                    var value = line.Substring(separatorIndex + 1).Trim();
                    bool isEnabled;
                    if (pluginName.Length == 0 || !TryParseBoolean(value, out isEnabled))
                    {
                        continue;
                    }

                    pluginStates[pluginName] = isEnabled;
                }

                return new PluginToggleSettings(pluginStates);
            }
            catch (Exception ex)
            {
                ErrorLogger.Instance.Write(string.Format("Failed to read plugin toggles: {0}\r\n{1}", ex.Message, ex.StackTrace));
                return new PluginToggleSettings(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
            }
        }

        public bool IsEnabled(string pluginName)
        {
            bool isEnabled;
            return !_pluginStates.TryGetValue(pluginName, out isEnabled) || isEnabled;
        }

        private static bool TryParseBoolean(string value, out bool result)
        {
            if (bool.TryParse(value, out result))
            {
                return true;
            }

            switch (value.ToLowerInvariant())
            {
                case "1":
                case "on":
                case "yes":
                    result = true;
                    return true;
                case "0":
                case "off":
                case "no":
                    result = false;
                    return true;
                default:
                    result = true;
                    return false;
            }
        }
    }
}
