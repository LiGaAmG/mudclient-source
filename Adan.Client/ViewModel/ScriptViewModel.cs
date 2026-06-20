namespace Adan.Client.ViewModel
{
    using Common.Model;
    using Common.Scripting;
    using Common.Utils;
    using Common.ViewModel;

    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    /// <summary>
    /// Wraps a single ScriptDefinition for binding in the Scripts dialog,
    /// plus live Start/Stop control over its LuaScriptHost coroutine.
    /// </summary>
    public class ScriptViewModel : ViewModelBase
    {
        private readonly ScriptDefinition _script;
        private readonly LuaScriptHost _scriptHost;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScriptViewModel"/> class.
        /// </summary>
        /// <param name="script">The script.</param>
        /// <param name="scriptHost">The script host.</param>
        public ScriptViewModel([NotNull] ScriptDefinition script, [NotNull] LuaScriptHost scriptHost)
        {
            Assert.ArgumentNotNull(script, "script");
            Assert.ArgumentNotNull(scriptHost, "scriptHost");
            _script = script;
            _scriptHost = scriptHost;

            StartCommand = new DelegateCommand(StartCommandExecute, true);
            StopCommand = new DelegateCommand(StopCommandExecute, true);
        }

        /// <summary>
        /// Gets the underlying script definition.
        /// </summary>
        [NotNull]
        public ScriptDefinition Script
        {
            get { return _script; }
        }

        /// <summary>
        /// Gets or sets the script name.
        /// </summary>
        public string Name
        {
            get { return _script.Name; }
            set
            {
                Assert.ArgumentNotNull(value, "value");
                _script.Name = value;
                OnPropertyChanged("Name");
            }
        }

        /// <summary>
        /// Gets or sets the script code.
        /// </summary>
        public string Code
        {
            get { return _script.Code; }
            set
            {
                Assert.ArgumentNotNull(value, "value");
                _script.Code = value;
                OnPropertyChanged("Code");
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this script is enabled
        /// (auto-started on connect).
        /// </summary>
        public bool IsEnabled
        {
            get { return _script.IsEnabled; }
            set
            {
                _script.IsEnabled = value;
                OnPropertyChanged("IsEnabled");
            }
        }

        /// <summary>
        /// Live runtime status -- NOT persisted. Call RefreshStatus()
        /// periodically (see ScriptsEditDialog's DispatcherTimer) to keep
        /// this current while the dialog is open.
        /// </summary>
        public ScriptRunStatus Status
        {
            get { return _scriptHost.GetScriptStatus(_script.Name); }
        }

        /// <summary>
        /// The Lua error message from the last time this script faulted
        /// (syntax error, runtime error, or watchdog timeout), or null if
        /// it never faulted. Bound as a tooltip on the Status text in the
        /// dialog -- "Faulted" alone doesn't say why.
        /// </summary>
        public string LastError
        {
            get { return _scriptHost.GetScriptError(_script.Name); }
        }

        /// <summary>
        /// Gets the start command.
        /// </summary>
        [NotNull]
        public DelegateCommand StartCommand
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the stop command.
        /// </summary>
        [NotNull]
        public DelegateCommand StopCommand
        {
            get;
            private set;
        }

        /// <summary>
        /// Re-reads Status from the host and raises PropertyChanged --
        /// call this from a UI timer, since LuaScriptHost has no change
        /// notification of its own.
        /// </summary>
        public void RefreshStatus()
        {
            OnPropertyChanged("Status");
            OnPropertyChanged("LastError");
        }

        private void StartCommandExecute(object obj)
        {
            _scriptHost.StartScript(_script.Name, _script.Code);
            RefreshStatus();
        }

        private void StopCommandExecute(object obj)
        {
            _scriptHost.StopScript(_script.Name);
            RefreshStatus();
        }
    }
}
