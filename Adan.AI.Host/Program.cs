using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Adan.AI.Host
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string pipeName = args.Length > 0 ? args[0] : "AdanAiPipe";
            string logPath = Path.Combine(AppContext.BaseDirectory, "ai-host.log");
            void Log(string msg) {
                var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
                Console.WriteLine(line);
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
            Log($"Started. PipeName={pipeName}");
            using var engine = new LlmEngine();
            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var server = new NamedPipeServerStream(
                        pipeName, PipeDirection.InOut, 10,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                        65536, 65536);

                    await server.WaitForConnectionAsync(cts.Token);
                    Log("Client connected");
                    _ = Task.Run(async () =>
                    {
                        using (server)
                            await HandleConnectionAsync(server, engine, cts.Token, Log);
                    });
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log($"Pipe error: {ex.Message}");
                    await Task.Delay(1000);
                }
            }
        }

        static async Task HandleConnectionAsync(NamedPipeServerStream pipe, LlmEngine engine, CancellationToken ct, Action<string> log)
        {
            log("HandleConnection started");
            var sb = new StringBuilder();
            var buf = new byte[4096];

            while (!ct.IsCancellationRequested)
            {
                int n;
                try
                {
                    n = await pipe.ReadAsync(buf, 0, buf.Length, ct);
                }
                catch (Exception ex) { log($"ReadAsync error: {ex.Message}"); break; }
                if (n == 0) break;

                sb.Append(Encoding.UTF8.GetString(buf, 0, n));

                string s = sb.ToString();
                int nl;
                while ((nl = s.IndexOf('\n')) >= 0)
                {
                    var line = s.Substring(0, nl).TrimEnd('\r').TrimStart((char)0xFEFF);
                    s = s.Substring(nl + 1);
                    sb.Clear(); sb.Append(s);

                    if (string.IsNullOrEmpty(line)) continue;
                    log($"Received: {line.Substring(0, Math.Min(200, line.Length))}");

                    string responseJson;
                    try
                    {
                        if (line.Contains("\"cmd\""))
                        {
                            var cmd = JsonSerializer.Deserialize<HostCommand>(line);
                            var resp = new HostResponse { Cmd = cmd!.Cmd };
                            try
                            {
                                switch (cmd.Cmd)
                                {
                                    case "load":
                                        log($"Loading model: {cmd.ModelPath}");
                                        engine.Load(cmd.ModelPath!, cmd.ContextSize, cmd.Threads,
                                            cmd.Temperature, cmd.TopP, cmd.RepeatPenalty);
                                        resp.Status = "loaded";
                                        log("Model loaded OK");
                                        break;
                                    case "unload":
                                        engine.Unload();
                                        resp.Status = "unloaded";
                                        break;
                                    case "status":
                                        resp.Status = engine.IsLoaded ? "ready" : "idle";
                                        break;
                                    default:
                                        resp.Status = "unknown_command";
                                        break;
                                }
                            }
                            catch (Exception ex) { resp.Status = "error"; resp.Error = ex.Message; log($"Command error: {ex.Message}"); }
                            responseJson = JsonSerializer.Serialize(resp);
                        }
                        else
                        {
                            var req = JsonSerializer.Deserialize<LlmRequest>(line);
                            if (req == null) continue;
                            if (req.Cancel) { engine.CancelCurrent(); continue; }
                            try
                            {
                                using var reqCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, reqCts.Token);
                                string text = await engine.GenerateAsync(req.Prompt, req.MaxTokens, linked.Token);
                                responseJson = JsonSerializer.Serialize(new LlmResponse { Id = req.Id, Text = text, Done = true });
                            }
                            catch (Exception ex)
                            {
                                responseJson = JsonSerializer.Serialize(new LlmResponse { Id = req.Id, Error = ex.Message, Done = true });
                            }
                        }

                        var respBytes = Encoding.UTF8.GetBytes(responseJson + "\n");
                        await pipe.WriteAsync(respBytes, 0, respBytes.Length, ct);
                        await pipe.FlushAsync(ct);
                    }
                    catch (Exception ex)
                    {
                        log($"Process error: {ex.Message}");
                    }
                }
            }
            log("HandleConnection ended");
        }
    }
}
