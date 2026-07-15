using System;
using System.Threading;
using System.Threading.Tasks;
using Adan.Client.Plugins.AI.Abstractions;

namespace Adan.Client.Plugins.AI.Tests.Fakes
{
    public class FakeLlmService : ILocalLlmService
    {
        public LlmStatus Status { get; private set; }
        public string FakeResponse { get; set; }
        public event EventHandler<LlmStatus> StatusChanged;

        public FakeLlmService()
        {
            Status = LlmStatus.Ready;
            FakeResponse = "Тестовый ответ";
        }

        public Task LoadModelAsync(CancellationToken ct = default(CancellationToken))
        {
            Status = LlmStatus.Ready;
            return Task.FromResult(0);
        }

        public void UnloadModel()
        {
            Status = LlmStatus.Disabled;
        }

        public Task<string> GenerateAsync(string prompt, CancellationToken ct = default(CancellationToken))
        {
            return Task.FromResult(FakeResponse);
        }

        public void CancelCurrent() { }

        public void Dispose() { }
    }
}
