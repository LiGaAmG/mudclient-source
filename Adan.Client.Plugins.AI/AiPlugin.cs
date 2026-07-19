using System.ComponentModel.Composition;
using System.Windows;
using Adan.Client.Common.Conveyor;
using Adan.Client.Common.Model;
using Adan.Client.Common.Plugins;
using Adan.Client.Common.ViewModel;
using Adan.Client.Plugins.AI.Commands;
using Adan.Client.Plugins.AI.Commentary;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Context;
using Adan.Client.Plugins.AI.Inference;
using Adan.Client.Plugins.AI.Events;
using Adan.Client.Plugins.AI.Lore;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI
{
    [Export(typeof(PluginBase))]
    public sealed class AiPlugin : PluginBase
    {
        private static AiSettings _settings;
        private static GameMemoryService _memory;
        private static LocalLlmService _llm;
        private static bool _initialized;
        private static bool _reindexStarted;
        private static readonly object _initLock = new object();

        public override string Name
        {
            get { return "LocalAI"; }
        }

        public override void InitializeConveyor(MessageConveyor conveyor)
        {
            lock (_initLock)
            {
                if (!_initialized)
                {
                    _initialized = true;
                    _settings = AiSettingsSerializer.Load();

                    if (_settings.Enabled)
                    {
                        _memory = new GameMemoryService(_settings);
                        try { _memory.Initialize(); } catch { }
                        AiLogger.Init(System.IO.Path.GetDirectoryName(_settings.ResolvedDatabasePath));
                        AiLogger.Log("INIT", "AI plugin started. DB=" + _settings.ResolvedDatabasePath);

                        _llm = new LocalLlmService(_settings);
                        // model loads on explicit ии вкл
                    }
                }
            }

            if (_settings == null) _settings = AiSettingsSerializer.Load();

            var session = new GameSessionState();
            var lore = new LoreSearchService(_settings, _memory);
            var contextBuilder = new AiContextBuilder(_settings, _memory, lore);
            var commandHandler = new AiCommandHandler(_llm, contextBuilder, _memory, lore, session, _settings, conveyor);
            var commentary = new AiCommentaryService(_settings, _llm, contextBuilder, conveyor);

            conveyor.AddConveyorUnit(new AiConveyorUnit(conveyor, commandHandler, commentary, new GameEventExtractor(), session, _memory, _settings, _llm));

            // MEF вызывает InitializeConveyor для каждого инстанса — реиндекс лора нужен один
            bool startReindex = false;
            lock (_initLock)
            {
                if (_settings.Enabled && !_reindexStarted) { _reindexStarted = true; startReindex = true; }
            }
            if (startReindex)
            {
                var cap = conveyor;
                System.Threading.Tasks.Task.Run(() => lore.ReindexAll(msg =>
                    cap.PushMessage(new Adan.Client.Common.Messages.OutputToMainWindowMessage(
                        msg, Adan.Client.Common.Themes.TextColor.BrightCyan))));
            }
        }

        public override void Initialize(InitializationStatusModel initializationStatusModel, Window MainWindowEx)
        {
            initializationStatusModel.CurrentPluginName = "Local AI";
            initializationStatusModel.PluginInitializationStatus = "OK";
        }

        public override void Dispose()
        {
            if (_llm != null)
                _llm.Dispose();
            if (_memory != null)
                _memory.Dispose();
            base.Dispose();
        }
    }
}
