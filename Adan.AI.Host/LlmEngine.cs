using LLama;
using LLama.Common;
using LLama.Sampling;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Adan.AI.Host
{
    public class LlmEngine : IDisposable
    {
        private LLamaWeights? _weights;
        private LLamaContext? _ctx;
        private ModelParams? _loadedParams;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private CancellationTokenSource? _currentCts;

        // Inference settings (set at Load time, used per request)
        private float _temperature = 0.6f;
        private float _topP = 0.9f;
        private float _repeatPenalty = 1.1f;

        public bool IsLoaded => _weights != null;

        public void Load(string modelPath, int contextSize, int threads,
            float temperature = 0.6f, float topP = 0.9f, float repeatPenalty = 1.1f)
        {
            Unload();
            _temperature = temperature;
            _topP = topP;
            _repeatPenalty = repeatPenalty;

            _loadedParams = new ModelParams(modelPath)
            {
                ContextSize = (uint)contextSize,
                Threads = threads,
                GpuLayerCount = 0,
            };
            _weights = LLamaWeights.LoadFromFile(_loadedParams);
            _ctx = _weights.CreateContext(_loadedParams);
        }

        public void Unload()
        {
            _currentCts?.Cancel();
            _ctx?.Dispose(); _ctx = null;
            _weights?.Dispose(); _weights = null;
            _loadedParams = null;
        }

        public async Task<string> GenerateAsync(string prompt, int maxTokens,
            CancellationToken externalCt)
        {
            if (_weights == null || _loadedParams == null)
                throw new InvalidOperationException("Model not loaded");

            await _lock.WaitAsync(externalCt);
            _currentCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            try
            {
                // Fresh context per call to avoid accumulated state issues
                using var ctx = _weights.CreateContext(_loadedParams);
                var executor = new InteractiveExecutor(ctx);
                var inferParams = new InferenceParams
                {
                    MaxTokens = maxTokens,
                    AntiPrompts = new List<string> { "<|im_end|>", "<|im_start|>", "\nUser:", "User:", "Вопрос:", "\n[" },
                    SamplingPipeline = new DefaultSamplingPipeline
                    {
                        Temperature = _temperature,
                        TopP = _topP,
                        RepeatPenalty = _repeatPenalty,
                    }
                };
                var sb = new StringBuilder();
                await foreach (var token in executor.InferAsync(prompt, inferParams, _currentCts.Token))
                    sb.Append(token);
                return sb.ToString().Trim();
            }
            finally
            {
                _currentCts = null;
                _lock.Release();
            }
        }

        public void CancelCurrent() => _currentCts?.Cancel();

        public void Dispose()
        {
            Unload();
            _lock.Dispose();
        }
    }
}
