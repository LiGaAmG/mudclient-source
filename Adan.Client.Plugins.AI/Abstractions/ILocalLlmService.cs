using System;
using System.Threading;
using System.Threading.Tasks;

namespace Adan.Client.Plugins.AI.Abstractions
{
    public enum LlmStatus { Disabled, ModelNotFound, Loading, Ready, Generating, Error }

    public interface ILocalLlmService : IDisposable
    {
        LlmStatus Status { get; }
        event EventHandler<LlmStatus> StatusChanged;
        Task LoadModelAsync(CancellationToken ct = default(CancellationToken));
        void UnloadModel();
        Task<string> GenerateAsync(string prompt, CancellationToken ct = default(CancellationToken));
        void CancelCurrent();
    }
}
