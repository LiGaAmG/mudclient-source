namespace Adan.Client.ViewModel
{
    using Common.Model;
    using Common.ViewModel;

    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    /// <summary>
    /// Wraps a single ScriptDefinition for binding in the Scripts dialog.
    /// </summary>
    public class ScriptViewModel : ViewModelBase
    {
        private readonly ScriptDefinition _script;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScriptViewModel"/> class.
        /// </summary>
        /// <param name="script">The script.</param>
        public ScriptViewModel([NotNull] ScriptDefinition script)
        {
            Assert.ArgumentNotNull(script, "script");
            _script = script;
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
        /// Gets or sets a value indicating whether this script is enabled.
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
        /// Gets or sets whether this script registers an on_group_state
        /// handler. Independent of the other two -- check any combination.
        /// </summary>
        public bool HandleGroupState
        {
            get { return _script.HandleGroupState; }
            set
            {
                _script.HandleGroupState = value;
                OnPropertyChanged("HandleGroupState");
            }
        }

        /// <summary>
        /// Gets or sets whether this script registers an on_room_state
        /// handler. Independent of the other two -- check any combination.
        /// </summary>
        public bool HandleRoomState
        {
            get { return _script.HandleRoomState; }
            set
            {
                _script.HandleRoomState = value;
                OnPropertyChanged("HandleRoomState");
            }
        }

        /// <summary>
        /// Gets or sets whether this script registers an on_room_change
        /// handler. Independent of the other two -- check any combination.
        /// </summary>
        public bool HandleRoomChange
        {
            get { return _script.HandleRoomChange; }
            set
            {
                _script.HandleRoomChange = value;
                OnPropertyChanged("HandleRoomChange");
            }
        }
    }
}
