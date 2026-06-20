namespace Adan.Client.ViewModel
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;

    using Common.Model;
    using Common.Utils;
    using Common.ViewModel;

    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    /// <summary>
    /// View model for the Scripts editor dialog -- a flat list of global
    /// Lua scripts, not nested under any trigger/alias Group.
    /// </summary>
    public class ScriptsViewModel : ViewModelBase
    {
        private readonly List<ScriptDefinition> _backingList;
        private ScriptViewModel _selectedScript;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScriptsViewModel"/> class.
        /// </summary>
        /// <param name="backingList">The backing list.</param>
        public ScriptsViewModel([NotNull] List<ScriptDefinition> backingList)
        {
            Assert.ArgumentNotNull(backingList, "backingList");

            _backingList = backingList;
            Scripts = new ObservableCollection<ScriptViewModel>(
                backingList.Select(s => new ScriptViewModel(s)));

            AddScriptCommand = new DelegateCommand(AddScriptCommandExecute, true);
            DeleteScriptCommand = new DelegateCommand(DeleteScriptCommandExecute, false);
        }

        /// <summary>
        /// Gets the scripts.
        /// </summary>
        [NotNull]
        public ObservableCollection<ScriptViewModel> Scripts
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets or sets the selected script.
        /// </summary>
        [CanBeNull]
        public ScriptViewModel SelectedScript
        {
            get { return _selectedScript; }
            set
            {
                _selectedScript = value;
                DeleteScriptCommand.CanBeExecuted = value != null;
                OnPropertyChanged("SelectedScript");
            }
        }

        /// <summary>
        /// Gets the add script command.
        /// </summary>
        [NotNull]
        public DelegateCommand AddScriptCommand
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the delete script command.
        /// </summary>
        [NotNull]
        public DelegateCommand DeleteScriptCommand
        {
            get;
            private set;
        }

        private void AddScriptCommandExecute(object obj)
        {
            var newScript = new ScriptDefinition { Name = "New script" };
            _backingList.Add(newScript);
            var newViewModel = new ScriptViewModel(newScript);
            Scripts.Add(newViewModel);
            SelectedScript = newViewModel;
        }

        private void DeleteScriptCommandExecute(object obj)
        {
            if (SelectedScript == null)
            {
                return;
            }

            _backingList.Remove(SelectedScript.Script);
            Scripts.Remove(SelectedScript);
            SelectedScript = null;
        }
    }
}
