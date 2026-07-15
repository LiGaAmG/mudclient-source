using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Adan.Client.Plugins.AI.Abstractions;
using Adan.Client.Plugins.AI.Configuration;

namespace Adan.Client.Plugins.AI.Inference
{
    public sealed class LocalLlmService : ILocalLlmService
    {
        private readonly AiSettings _settings;
        private LlmStatus _status = LlmStatus.Disabled;
        private string _lastError;
        private Process _process;
        private NamedPipeClientStream _pipe;
        private readonly object _lock = new object();
        private readonly StringBuilder _readBuf = new StringBuilder();
        private readonly byte[] _tmpBuf = new byte[4096];

        private static readonly DataContractJsonSerializer HostCmdSerializer =
            new DataContractJsonSerializer(typeof(HostCommand));
        private static readonly DataContractJsonSerializer HostRespSerializer =
            new DataContractJsonSerializer(typeof(HostResponse));
        private static readonly DataContractJsonSerializer LlmReqSerializer =
            new DataContractJsonSerializer(typeof(LlmRequest));
        private static readonly DataContractJsonSerializer LlmRespSerializer =
            new DataContractJsonSerializer(typeof(LlmResponse));

        public string LastError { get { return _lastError; } }

        public LlmStatus Status
        {
            get { return _status; }
            private set
            {
                if (_status == value) return;
                _status = value;
                StatusChanged?.Invoke(this, value);
            }
        }

        public event EventHandler<LlmStatus> StatusChanged;

        public LocalLlmService(AiSettings settings)
        {
            _settings = settings;
        }

        public async Task LoadModelAsync(CancellationToken ct = default(CancellationToken))
        {
            var modelPath = _settings.ResolvedModelPath;
            if (!File.Exists(modelPath))
            {
                Status = LlmStatus.ModelNotFound;
                return;
            }

            if (_status == LlmStatus.Loading || _status == LlmStatus.Ready) return;
            CleanupPipe();
            Status = LlmStatus.Loading;
            const string pipeName = "AdanAiPipe";
            try
            {
                using (var mutex = new System.Threading.Mutex(false, "AdanAiHostStart"))
                {
                    mutex.WaitOne();
                    try
                    {
                        if (Process.GetProcessesByName("Adan.AI.Host").Length == 0)
                        {
                            var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AI", "Adan.AI.Host.exe");
                            var psi = new ProcessStartInfo(exePath, pipeName)
                            {
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            _process = Process.Start(psi);
                            System.Threading.Thread.Sleep(1500);
                        }
                    }
                    finally { mutex.ReleaseMutex(); }
                }

                _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

                // Retry connect 20 times x 500ms
                bool connected = false;
                for (int i = 0; i < 20; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        await Task.Run(() => _pipe.Connect(500), ct).ConfigureAwait(false);
                        connected = true;
                        break;
                    }
                    catch (TimeoutException) { }
                    await Task.Delay(500, ct).ConfigureAwait(false);
                }

                if (!connected)
                {
                    _lastError = "Не удалось подключиться к AI.Host.exe. Проверь что .NET 8 Runtime (x64) установлен.";
                    Status = LlmStatus.Error;
                    return;
                }

                // Send load command (model loading can take 30+ seconds)
                var cmd = new HostCommand
                {
                    Cmd = "load",
                    ModelPath = modelPath,
                    ContextSize = _settings.ContextSize,
                    Threads = _settings.Threads,
                    Temperature = _settings.Temperature,
                    TopP = _settings.TopP,
                    RepeatPenalty = _settings.RepeatPenalty
                };
                await WriteJsonPipeAsync(cmd, HostCmdSerializer, ct).ConfigureAwait(false);

                using (var loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    loadCts.CancelAfter(TimeSpan.FromSeconds(120));
                    var resp = await ReadJsonPipeAsync<HostResponse>(HostRespSerializer, loadCts.Token).ConfigureAwait(false);
                    if (resp != null && resp.Status == "loaded")
                    {
                        _lastError = null;
                        Status = LlmStatus.Ready;
                    }
                    else
                    {
                        _lastError = resp != null ? resp.Error : "Нет ответа от AI.Host.exe";
                        Status = LlmStatus.Error;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Status = LlmStatus.Disabled;
                throw;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                Status = LlmStatus.Error;
            }
        }

        public void UnloadModel()
        {
            try
            {
                if (_pipe != null && _pipe.IsConnected)
                {
                    var cmd = new HostCommand { Cmd = "unload" };
                    WriteJsonPipeSync(cmd, HostCmdSerializer);
                    ReadJsonPipeSync<HostResponse>(HostRespSerializer);
                }
            }
            catch { }
            finally
            {
                CleanupPipe();
                Status = LlmStatus.Disabled;
            }
        }

        public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default(CancellationToken))
        {
            if (Status != LlmStatus.Ready)
                return null;

            Status = LlmStatus.Generating;
            try
            {
                var req = new LlmRequest
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Prompt = prompt,
                    MaxTokens = _settings.MaxResponseTokens,
                    Cancel = false
                };
                await WriteJsonPipeAsync(req, LlmReqSerializer, ct).ConfigureAwait(false);
                var resp = await ReadJsonPipeAsync<LlmResponse>(LlmRespSerializer, ct).ConfigureAwait(false);
                Status = LlmStatus.Ready;
                if (resp != null && string.IsNullOrEmpty(resp.Error))
                    return resp.Text;
                return null;
            }
            catch (OperationCanceledException)
            {
                Status = LlmStatus.Ready;
                throw;
            }
            catch
            {
                Status = LlmStatus.Ready;
                return null;
            }
        }

        public void CancelCurrent()
        {
            try
            {
                if (_pipe != null && _pipe.IsConnected)
                {
                    var req = new LlmRequest { Cancel = true };
                    WriteJsonPipeSync(req, LlmReqSerializer);
                }
            }
            catch { }
        }

        // Write JSON as newline-terminated bytes directly to pipe
        private async Task WriteJsonPipeAsync<T>(T obj, DataContractJsonSerializer serializer, CancellationToken ct)
        {
            string json;
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, obj);
                json = Encoding.UTF8.GetString(ms.ToArray());
            }
            var bytes = new UTF8Encoding(false).GetBytes(json + "\n");
            await _pipe.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
            await _pipe.FlushAsync(ct).ConfigureAwait(false);
        }

        private void WriteJsonPipeSync<T>(T obj, DataContractJsonSerializer serializer)
        {
            string json;
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, obj);
                json = Encoding.UTF8.GetString(ms.ToArray());
            }
            var bytes = new UTF8Encoding(false).GetBytes(json + "\n");
            _pipe.Write(bytes, 0, bytes.Length);
            _pipe.Flush();
        }

        // Read one newline-terminated line from pipe using direct ReadAsync
        private async Task<T> ReadJsonPipeAsync<T>(DataContractJsonSerializer serializer, CancellationToken ct)
            where T : class
        {
            while (true)
            {
                string s = _readBuf.ToString();
                int nl = s.IndexOf('\n');
                if (nl >= 0)
                {
                    var line = s.Substring(0, nl).TrimEnd('\r');
                    _readBuf.Remove(0, nl + 1);
                    if (string.IsNullOrEmpty(line)) return null;
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                        return (T)serializer.ReadObject(ms);
                }

                int n = await _pipe.ReadAsync(_tmpBuf, 0, _tmpBuf.Length, ct).ConfigureAwait(false);
                if (n == 0) return null;
                _readBuf.Append(Encoding.UTF8.GetString(_tmpBuf, 0, n));
            }
        }

        private T ReadJsonPipeSync<T>(DataContractJsonSerializer serializer) where T : class
        {
            while (true)
            {
                string s = _readBuf.ToString();
                int nl = s.IndexOf('\n');
                if (nl >= 0)
                {
                    var line = s.Substring(0, nl).TrimEnd('\r');
                    _readBuf.Remove(0, nl + 1);
                    if (string.IsNullOrEmpty(line)) return null;
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                        return (T)serializer.ReadObject(ms);
                }

                int n = _pipe.Read(_tmpBuf, 0, _tmpBuf.Length);
                if (n == 0) return null;
                _readBuf.Append(Encoding.UTF8.GetString(_tmpBuf, 0, n));
            }
        }

        private void CleanupPipe()
        {
            try { _pipe?.Dispose(); } catch { }
            try { if (_process != null && !_process.HasExited) _process.Kill(); } catch { }
            try { _process?.Dispose(); } catch { }
            _readBuf.Clear();
            _pipe = null;
            _process = null;
        }

        public void Dispose()
        {
            UnloadModel();
        }
    }
}
