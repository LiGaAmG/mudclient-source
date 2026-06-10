using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Adan.Client.Common.Commands;
using Adan.Client.Common.Conveyor;
using Adan.Client.Common.ConveyorUnits;
using Adan.Client.Common.Messages;
using Adan.Client.Common.Model;
using CSLib.Net.Annotations;
using CSLib.Net.Diagnostics;

namespace Adan.Client.Plugins.SpellManager
{
    /// <summary>
    /// Conveyor unit that observes TextMessages and forwards them to SpellTabModel.
    /// Батчит строки: в очереди диспетчера всегда не более одного BeginInvoke.
    /// </summary>
    public class SpellConveyorUnit : ConveyorUnit
    {
        private readonly SpellTabModel _model;

        // Батч строк: накапливаем на фоновом потоке, обрабатываем одним BeginInvoke
        private readonly object _lock = new object();
        private readonly List<string> _pendingLines = new List<string>();
        private bool _dispatchPending = false;

        // Флаг активной секции зауч/закл. Ставится при отправке команды,
        // сбрасывается автоматически через 3 секунды (таймер).
        private volatile bool _bgInSection = false;
        private int _sectionGeneration = 0; // защита от гонки: старый таймер не сбросит новый флаг
        private System.Threading.Timer _sectionTimer;

        public SpellConveyorUnit([NotNull] SpellTabModel model, [NotNull] MessageConveyor conveyor)
            : base(conveyor)
        {
            Assert.ArgumentNotNull(model, "model");
            _model = model;
        }

        public override IEnumerable<int> HandledMessageTypes
        {
            get { return new[] { BuiltInMessageTypes.TextMessage }; }
        }

        public override IEnumerable<int> HandledCommandTypes
        {
            get { return new[] { BuiltInCommandTypes.TextCommand }; }
        }

        public override void HandleCommand([NotNull] Command command, bool isImport = false)
        {
            Assert.ArgumentNotNull(command, "command");

            var textCommand = command as TextCommand;
            if (textCommand == null) return;

            var text = textCommand.CommandText.Trim();

            if (string.Equals(text, "мем", System.StringComparison.OrdinalIgnoreCase))
            {
                textCommand.Handled = true;
                var rootModel = Conveyor.RootModel;
                Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Normal,
                    new System.Action(() => _model.ForgetAndRememorize(rootModel)));
                return;
            }

            if (string.Equals(text, "мем план", System.StringComparison.OrdinalIgnoreCase))
            {
                textCommand.Handled = true;
                var rootModel = Conveyor.RootModel;
                Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Normal,
                    new System.Action(() => _model.ExecutePlan(rootModel)));
                return;
            }

            // Если игрок отправляет зауч или закл — ожидаем многострочный ответ.
            // Выставляем флаг ДО прихода ответа, чтобы строки секции не отфильтровались.
            // Через 3 секунды флаг сбросится автоматически — не зависим от терминаторов.
            var lower = text.ToLowerInvariant();
            bool isZauch = lower.StartsWith("зауч") || lower.StartsWith("зау");
            bool isZakl  = lower.StartsWith("закл") || lower.StartsWith("заклинания");
            if (isZauch || isZakl)
            {
                SetSectionFlag(true);
            }
        }

        public override void HandleMessage([NotNull] Message message)
        {
            Assert.ArgumentNotNull(message, "message");

            var textMessage = message as TextMessage;
            if (textMessage == null) return;

            var line = textMessage.InnerText;
            if (line == null) return;

            // Быстрая фильтрация: боевые сообщения, передвижение, промпты отсекаются здесь.
            if (!_bgInSection && !CouldBeRelevant(line))
                return;

            bool needDispatch;
            lock (_lock)
            {
                _pendingLines.Add(line);
                needDispatch = !_dispatchPending;
                if (needDispatch) _dispatchPending = true;
            }

            if (needDispatch)
            {
                var rootModel = Conveyor.RootModel;
                Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Normal,
                    new System.Action(() => FlushLines(rootModel)));
            }
        }

        private void SetSectionFlag(bool value)
        {
            _bgInSection = value;

            if (value)
            {
                // Увеличиваем поколение — старый таймер по истечении увидит несовпадение и не сбросит флаг
                int gen = Interlocked.Increment(ref _sectionGeneration);
                var t = _sectionTimer;
                if (t != null) t.Dispose();
                _sectionTimer = new System.Threading.Timer(_ =>
                {
                    if (_sectionGeneration == gen)
                        SetSectionFlag(false);
                }, null, 3000, System.Threading.Timeout.Infinite);
            }

            // Обновляем UI-свойство на диспетчере
            var model = _model;
            Application.Current.Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new System.Action(() => model.IsInSection = value));
        }

        /// <summary>
        /// Быстрая проверка: может ли строка что-то изменить в модели заклинаний.
        /// Вызывается на фоновом потоке — только простые сравнения, никаких regex.
        /// </summary>
        private static bool CouldBeRelevant(string line)
        {
            if (line.Length == 0) return false;

            if (line.StartsWith("Вы теперь готовы произнести", System.StringComparison.Ordinal)) return true;
            if (line.StartsWith("Вы произнесли магические слова", System.StringComparison.Ordinal)) return true;
            if (line.StartsWith("Вы успешно забыли заклинание", System.StringComparison.Ordinal)) return true;
            if (line.StartsWith("Вы добавили заклинание", System.StringComparison.Ordinal)) return true;
            if (line.StartsWith("Вы убрали заклинание", System.StringComparison.Ordinal)) return true;
            if (line == "Вы умерли.") return true;

            if (line == "Заученные заклинания:") return true;
            if (line == "У вас нет заученных заклинаний.") return true;
            if (line == "Ваши заклинания:") return true;
            if (line == "Список заклинаний для запоминания:") return true;

            return false;
        }

        private void FlushLines(RootModel rootModel)
        {
            List<string> lines;
            lock (_lock)
            {
                lines = new List<string>(_pendingLines);
                _pendingLines.Clear();
                _dispatchPending = false;
            }

            foreach (var line in lines)
                _model.HandleLine(line, rootModel);
        }
    }
}
