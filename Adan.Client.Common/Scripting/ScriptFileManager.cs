using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;

namespace Adan.Client.Common.Scripting
{
    /// <summary>
    /// Manages a folder of .lua script files. Watches for changes,
    /// loads/saves metadata from an adjacent <c>name.script.json</c> file.
    /// Thread-safe for reads; writes happen on the calling thread.
    /// </summary>
    public class ScriptFileManager : IDisposable
    {
        private readonly string _folder;
        private readonly object _lock = new object();
        private FileSystemWatcher _watcher;
        private List<ScriptFileEntry> _entries = new List<ScriptFileEntry>();
        private readonly Dictionary<string, System.Threading.CancellationTokenSource> _pendingFileChanges = new Dictionary<string, System.Threading.CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);

        public event EventHandler ScriptsChanged;
        public event EventHandler<ScriptFileChangedEventArgs> ScriptFileChanged;

        public string Folder => _folder;

        public ScriptFileManager(string folder, bool watchForChanges = true)
        {
            _folder = folder;
            Directory.CreateDirectory(folder);
            Reload();
            if (watchForChanges) StartWatcher();
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

                // A Lua file drives the list. Its metadata travels beside it.
                var newEntries = new List<ScriptFileEntry>();
                foreach (var file in files)
                {
                    var metadata = LoadMetadata(file);
                    newEntries.Add(new ScriptFileEntry
                    {
                        FileName = file,
                        IsGlobal = metadata.IsGlobal,
                        AutoStart = metadata.AutoStart,
                        EnabledProfileNames = metadata.ProfileNames ?? new List<string>()
                    });
                }

                _entries = newEntries;
            }
        }

        public void SaveMetadata()
        {
            lock (_lock)
            {
                foreach (var entry in _entries)
                    SaveMetadata(entry);
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

        public IReadOnlyList<ScriptFileEntry> GetApplicableScripts(string profileName)
        {
            lock (_lock)
            {
                return _entries.Where(e => e.IsGlobal || e.EnabledProfileNames.Contains(profileName)).ToList();
            }
        }

        private string GetMetadataPath(string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            return Path.Combine(_folder, name + ".script.json");
        }

        private ScriptFileMetadata LoadMetadata(string fileName)
        {
            var path = GetMetadataPath(fileName);
            if (!File.Exists(path)) return new ScriptFileMetadata { Version = 1, ProfileNames = new List<string>() };
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(ScriptFileMetadata));
                    var result = (ScriptFileMetadata)serializer.ReadObject(stream);
                    result.ProfileNames = result.ProfileNames ?? new List<string>();
                    return result;
                }
            }
            catch { return new ScriptFileMetadata { Version = 1, ProfileNames = new List<string>() }; }
        }

        private void SaveMetadata(ScriptFileEntry entry)
        {
            var metadata = new ScriptFileMetadata
            {
                Version = 1,
                IsGlobal = entry.IsGlobal,
                AutoStart = entry.AutoStart,
                ProfileNames = entry.EnabledProfileNames ?? new List<string>()
            };
            using (var stream = File.Create(GetMetadataPath(entry.FileName)))
                new DataContractJsonSerializer(typeof(ScriptFileMetadata)).WriteObject(stream, metadata);
        }

        private void StartWatcher()
        {
            if (!Directory.Exists(_folder)) return;
            _watcher = new FileSystemWatcher(_folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            _watcher.Filter = "*.*";
            _watcher.Created += OnFsChange;
            _watcher.Changed += OnFsChange;
            _watcher.Deleted += OnFsChange;
            _watcher.Renamed += OnFsChange;
        }

        private void OnFsChange(object sender, FileSystemEventArgs e)
        {
            if (!e.FullPath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) &&
                !e.FullPath.EndsWith(".script.json", StringComparison.OrdinalIgnoreCase)) return;

            System.Threading.CancellationTokenSource cancellation;
            lock (_lock)
            {
                System.Threading.CancellationTokenSource previous;
                if (_pendingFileChanges.TryGetValue(e.FullPath, out previous)) previous.Cancel();
                cancellation = new System.Threading.CancellationTokenSource();
                _pendingFileChanges[e.FullPath] = cancellation;
            }

            System.Threading.Tasks.Task.Delay(300, cancellation.Token).ContinueWith(_ =>
            {
                if (_.IsCanceled) return;
                lock (_lock) { _pendingFileChanges.Remove(e.FullPath); }
                Reload();
                ScriptsChanged?.Invoke(this, EventArgs.Empty);
                if (e.FullPath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                    ScriptFileChanged?.Invoke(this, new ScriptFileChangedEventArgs(Path.GetFileName(e.FullPath)));
            });
        }

        public void Dispose()
        {
            _watcher?.Dispose();
            lock (_lock)
            {
                foreach (var pending in _pendingFileChanges.Values) pending.Cancel();
                _pendingFileChanges.Clear();
            }
        }
    }

    public sealed class ScriptFileChangedEventArgs : EventArgs
    {
        public ScriptFileChangedEventArgs(string fileName) { FileName = fileName; }
        public string FileName { get; private set; }
    }
}
