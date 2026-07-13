using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace Adan.Client.Common.Scripting
{
    /// <summary>
    /// Manages a folder of .lua script files. Watches for changes,
    /// loads/saves metadata (IsShared, EnabledTabUids) from scripts.xml.
    /// Thread-safe for reads; writes happen on the calling thread.
    /// </summary>
    public class ScriptFileManager : IDisposable
    {
        private const string MetadataFileName = "scripts.xml";
        private static readonly XmlSerializer _serializer = new XmlSerializer(typeof(ScriptFileMetadata));

        private readonly string _folder;
        private readonly object _lock = new object();
        private FileSystemWatcher _watcher;
        private List<ScriptFileEntry> _entries = new List<ScriptFileEntry>();

        public event EventHandler ScriptsChanged;

        public string Folder => _folder;

        public ScriptFileManager(string folder)
        {
            _folder = folder;
            Directory.CreateDirectory(folder);
            Reload();
            StartWatcher();
        }

        public IReadOnlyList<ScriptFileEntry> Entries
        {
            get { lock (_lock) { return _entries.ToList(); } }
        }

        public void Reload()
        {
            lock (_lock)
            {
                var files = Directory.Exists(_folder)
                    ? Directory.GetFiles(_folder, "*.lua").Select(Path.GetFileName).OrderBy(f => f).ToList()
                    : new List<string>();

                var meta = LoadMetadata();

                // Build entry list: file on disk drives the list, metadata fills in flags
                var newEntries = new List<ScriptFileEntry>();
                foreach (var file in files)
                {
                    var existing = meta.Entries.FirstOrDefault(e => string.Equals(e.FileName, file, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                        newEntries.Add(existing);
                    else
                        newEntries.Add(new ScriptFileEntry { FileName = file, IsShared = false, AutoStart = false });
                }

                _entries = newEntries;
            }
        }

        public void SaveMetadata()
        {
            lock (_lock)
            {
                var meta = new ScriptFileMetadata { Entries = _entries.ToList() };
                var path = Path.Combine(_folder, MetadataFileName);
                using (var stream = File.Create(path))
                    _serializer.Serialize(stream, meta);
            }
        }

        public string GetFilePath(string fileName) => Path.Combine(_folder, fileName);

        public string CreateScript(string name)
        {
            if (!name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                name += ".lua";
            var path = Path.Combine(_folder, name);
            if (!File.Exists(path))
                File.WriteAllText(path, "-- " + name + "\n\nwhile true do\n    local text = WaitText()\nend\n");
            Reload();
            SaveMetadata();
            return path;
        }

        public void DeleteScript(string fileName)
        {
            var path = Path.Combine(_folder, fileName);
            if (File.Exists(path))
                File.Delete(path);
            Reload();
            SaveMetadata();
        }

        public string ReadCode(string fileName) =>
            File.Exists(GetFilePath(fileName)) ? File.ReadAllText(GetFilePath(fileName)) : string.Empty;

        public void WriteCode(string fileName, string code) =>
            File.WriteAllText(GetFilePath(fileName), code);

        private ScriptFileMetadata LoadMetadata()
        {
            var path = Path.Combine(_folder, MetadataFileName);
            if (!File.Exists(path)) return new ScriptFileMetadata();
            try
            {
                using (var stream = File.OpenRead(path))
                    return (ScriptFileMetadata)_serializer.Deserialize(stream);
            }
            catch { return new ScriptFileMetadata(); }
        }

        private void StartWatcher()
        {
            if (!Directory.Exists(_folder)) return;
            _watcher = new FileSystemWatcher(_folder, "*.lua")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnFsChange;
            _watcher.Deleted += OnFsChange;
            _watcher.Renamed += OnFsChange;
        }

        private void OnFsChange(object sender, FileSystemEventArgs e)
        {
            System.Threading.Tasks.Task.Delay(200).ContinueWith(_ =>
            {
                Reload();
                ScriptsChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        public void Dispose()
        {
            _watcher?.Dispose();
        }
    }
}
