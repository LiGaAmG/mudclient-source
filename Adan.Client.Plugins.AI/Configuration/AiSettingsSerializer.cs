using System;
using System.IO;
using System.Xml.Serialization;
using Adan.Client.Common.Settings;

namespace Adan.Client.Plugins.AI.Configuration
{
    public static class AiSettingsSerializer
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(AiSettings));

        private static string SettingsPath
        {
            get { return Path.Combine(SettingsHolder.Instance.Folder, "ai-settings.xml"); }
        }

        public static AiSettings Load()
        {
            var path = SettingsPath;
            if (!File.Exists(path))
                return new AiSettings();
            try
            {
                using (var reader = File.OpenRead(path))
                    return (AiSettings)Serializer.Deserialize(reader);
            }
            catch
            {
                return new AiSettings();
            }
        }

        public static void Save(AiSettings settings)
        {
            var path = SettingsPath;
            var dir = Path.GetDirectoryName(path);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            using (var writer = File.CreateText(path))
                Serializer.Serialize(writer, settings);
        }
    }
}
