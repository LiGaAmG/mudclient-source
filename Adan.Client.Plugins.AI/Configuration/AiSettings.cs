using System;
using System.IO;
using System.Xml.Serialization;
using Adan.Client.Common.Settings;

namespace Adan.Client.Plugins.AI.Configuration
{
    public enum AiOutputTarget
    {
        MainWindow,
        AdditionalOutput,
        AdditionalOutput2
    }

    [XmlRoot("LocalAi")]
    public class AiSettings
    {
        public bool Enabled { get; set; }
        public string ModelPath { get; set; }
        public int ContextSize { get; set; }
        public int MaxResponseTokens { get; set; }
        public int Threads { get; set; }
        public float Temperature { get; set; }
        public float TopP { get; set; }
        public float RepeatPenalty { get; set; }
        public bool CommentaryEnabled { get; set; }
        public int CommentaryCooldownSeconds { get; set; }
        public string LoreDirectory { get; set; }
        public string DatabasePath { get; set; }
        public string AssistantName { get; set; }
        public int RequestTimeoutSeconds { get; set; }
        public bool DebugLogPrompts { get; set; }
        public AiOutputTarget OutputTarget { get; set; }

        public AiSettings()
        {
            Enabled = false;
            ModelPath = @"Models\qwen3-1.7b-q4_k_m.gguf";
            ContextSize = 4096;
            MaxResponseTokens = 200;
            Threads = Math.Max(2, Environment.ProcessorCount / 2);
            Temperature = 0.6f;
            TopP = 0.9f;
            RepeatPenalty = 1.1f;
            CommentaryEnabled = true;
            CommentaryCooldownSeconds = 30;
            LoreDirectory = "Lore";
            DatabasePath = @"Data\ai-memory.db";
            AssistantName = "Лира";
            RequestTimeoutSeconds = 90;
            DebugLogPrompts = false;
            OutputTarget = AiOutputTarget.MainWindow;
        }

        // Base folder shared with triggers/aliases: Documents\Adan client\
        private static string UserDataFolder
        {
            get { return SettingsHolder.Instance.Folder; }
        }

        [XmlIgnore]
        public string ResolvedModelPath
        {
            get
            {
                return Path.IsPathRooted(ModelPath)
                    ? ModelPath
                    : Path.Combine(UserDataFolder, ModelPath);
            }
        }

        [XmlIgnore]
        public string ResolvedDatabasePath
        {
            get
            {
                return Path.IsPathRooted(DatabasePath)
                    ? DatabasePath
                    : Path.Combine(UserDataFolder, DatabasePath);
            }
        }

        [XmlIgnore]
        public string ResolvedLoreDirectory
        {
            get
            {
                return Path.IsPathRooted(LoreDirectory)
                    ? LoreDirectory
                    : Path.Combine(UserDataFolder, LoreDirectory);
            }
        }
    }
}
