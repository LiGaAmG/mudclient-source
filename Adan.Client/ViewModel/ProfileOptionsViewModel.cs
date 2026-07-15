using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Adan.Client.Common.Model;
using Adan.Client.Common.Settings;
using Adan.Client.Common.Utils;
using Adan.Client.Common.ViewModel;
using Adan.Client.Dialogs;
using Adan.Client.Model;
using CSLib.Net.Annotations;
using CSLib.Net.Diagnostics;
using Microsoft.Win32;

namespace Adan.Client.ViewModel
{
    /// <summary>
    /// 
    /// </summary>
    public class ProfileOptionsViewModel : ViewModelBase
    {
        private GroupsViewModel _groupsViewModel;
        private ListBoxItem _selectedOption;
        private readonly IList<RootModel> _allRootModels;

        /// <summary>
        ///
        /// </summary>
        /// <param name="profile"></param>
        public ProfileOptionsViewModel(ProfileHolder profile) : this(profile, null)
        {
        }

        /// <summary>
        /// </summary>
        /// <param name="profile"></param>
        /// <param name="allRootModels">
        /// Every currently-open tab's RootModel, so saving the Scripts
        /// dialog can call ReloadScripts() on any already-open tab using
        /// this same profile, instead of requiring a reconnect. Null is
        /// fine (e.g. when no tabs happen to be open at all) -- the Scripts
        /// case in EditProfile just skips the reload step then.
        /// </param>
        public ProfileOptionsViewModel(ProfileHolder profile, IList<RootModel> allRootModels)
        {
            _groupsViewModel = new GroupsViewModel(profile.Groups, profile.Name, RootModel.AllActionDescriptions);
            Profile = profile;
            _allRootModels = allRootModels;

            EditOptionsCommand = new DelegateCommand(EditProfile, true);
            ImportProfileCommand = new DelegateCommand(ImportProfile, true);
        }

        /// <summary>
        /// This constructor is supposed for creating global profile.
        /// Global profile cannot be imported for now
        /// </summary>
        /// <param name="name">Name of the profile (e.g. "Global")</param>
        /// <param name="groups">List of profile groups</param>
        public ProfileOptionsViewModel(string name, List<Group> groups) : this(name, groups, null)
        {
        }

        /// <summary>
        /// </summary>
        /// <param name="name">Name of the profile (e.g. "Global")</param>
        /// <param name="groups">List of profile groups</param>
        /// <param name="allRootModels">
        /// Every currently-open tab's RootModel, so editing a trigger in the global
        /// groups (which every profile's RootModel.Groups pulls in regardless of its
        /// own Profile.Name) can refresh the cached trigger list on all of them. Null
        /// is fine -- the refresh step just gets skipped, same as a closed-tabs session.
        /// </param>
        public ProfileOptionsViewModel(string name, List<Group> groups, IList<RootModel> allRootModels)
        {
            _groupsViewModel = new GroupsViewModel(groups, name, RootModel.AllActionDescriptions);
            Profile = null;
            _allRootModels = allRootModels;

            EditOptionsCommand = new DelegateCommand(EditProfile, true);
            ImportProfileCommand = new DelegateCommand(ImportProfile, true);
        }

        /// <summary>
        /// 
        /// </summary>
        public ProfileHolder Profile
        {
            get;
            set;
        }

        /// <summary>
        /// 
        /// </summary>
        public ListBoxItem SelectedOption
        {
            get
            {
                return _selectedOption;
            }
            set
            {
                if (_selectedOption != value)
                {
                    _selectedOption = value;
                    OnPropertyChanged("SelectedOption");
                    OnPropertyChanged("CanEditProfile");
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        [NotNull]
        public DelegateCommand EditOptionsCommand
        {
            get;
            private set;
        }

        /// <summary>
        /// 
        /// </summary>
        [NotNull]
        public DelegateCommand ImportProfileCommand
        {
            get;
            private set;
        }

        /// <summary>
        /// Amount of aliases in the profile
        /// </summary>
        public int AliasesCount { get { return _groupsViewModel.Groups.Sum(g => g.Aliases.Count); } }
        /// <summary>
        /// Amount of hotkeys in the profile
        /// </summary>
        public int HotkeysCount { get { return _groupsViewModel.Groups.Sum(g => g.Hotkeys.Count); } }
        /// <summary>
        /// Amount of groups in the profile
        /// </summary>
        public int GroupsCount { get { return _groupsViewModel.AllGroup.Count; } }
        /// <summary>
        /// Amount of highlights in the profile
        /// </summary>
        public int HighlightsCount { get { return _groupsViewModel.Groups.Sum(g => g.Highlights.Count); } }
        /// <summary>
        /// Amount of triggers in the profile
        /// </summary>
        public int TriggersCount { get { return _groupsViewModel.Groups.Sum(g => g.Triggers.Count); } }
        /// <summary>
        /// Amount of triggers in the profile
        /// </summary>
        public int SubstitutionsCount { get { return _groupsViewModel.Groups.Sum(g => g.Substitutions.Count); } }
        /// <summary>
        /// Amount of global scripts in the profile. Null for the synthetic
        /// "Global" profile (see the string/groups constructor above) --
        /// scripts are per-character, not part of the Group system.
        /// </summary>
        public int ScriptsCount { get { return Profile == null ? 0 : Profile.Scripts.Count; } }

        /// <summary>
        /// Can profile be imported or not
        /// </summary>
        public bool CanImportProfile { get { return Profile != null;  } }
        public bool CanEditProfile { get { return SelectedOption != null; } }
        private void EditProfile([NotNull] object obj)
        {
            Assert.ArgumentNotNull(obj, "obj");

            var owner = obj as Window;
            if (owner == null)
                return;

            var name = SelectedOption.Tag.ToString();
            switch (name)
            {
                case "Aliases":
                    var aliasesEditDialog = new AliasesEditDialog
                    {
                        DataContext = new AliasesViewModel(_groupsViewModel.Groups, RootModel.AllActionDescriptions),
                        Owner = owner
                    };
                    aliasesEditDialog.Closed += (s, e) =>
                    {
                        OnPropertyChanged("AliasesCount");
                        SettingsHolder.Instance.SetProfile(_groupsViewModel.Name);
                    };
                    aliasesEditDialog.Show();
                    break;
                case "Groups":
                    var groupEditDialog = new GroupsEditDialog
                    {
                        DataContext = _groupsViewModel,
                        Owner = owner
                    };
                    groupEditDialog.Closed += (s, e) =>
                    {
                        OnPropertyChanged("GroupsCount");
                        SettingsHolder.Instance.GetProfile(_groupsViewModel.Name).Groups = _groupsViewModel.AllGroup;
                        SettingsHolder.Instance.SetProfile(_groupsViewModel.Name);
                    };
                    groupEditDialog.Show();
                    break;
                case "Highlights":
                    var highlightsEditDialog = new HighlightsEditDialog
                    {
                        DataContext = new HighlightsViewModel(_groupsViewModel.Groups),
                        Owner = owner
                    };
                    highlightsEditDialog.Closed += (s, e) =>
                    {
                        OnPropertyChanged("HighlightsCount");
                        SettingsHolder.Instance.SetProfile(_groupsViewModel.Name);
                    };
                    highlightsEditDialog.Show();
                    break;
                case "Hotkeys":
                    var hotKeysEditDialog = new HotkeysEditDialog
                    {
                        DataContext = new HotkeysViewModel(_groupsViewModel.Groups, RootModel.AllActionDescriptions),
                        Owner = owner
                    };
                    hotKeysEditDialog.Closed += (s, e) =>
                    {
                        OnPropertyChanged("HotkeysCount");
                        SettingsHolder.Instance.SetProfile(_groupsViewModel.Name);
                    };
                    hotKeysEditDialog.Show();
                    break;
                case "Scripts":
                    var scriptsFolder = System.IO.Path.Combine(SettingsHolder.Instance.Folder, "scripts");
                    new Windows.ScriptsForm(new Adan.Client.Common.Scripting.ScriptFileManager(scriptsFolder), _allRootModels).Show();
                    break;
                case "Substitutions":
                    var substitutionsEditDialog = new SubstitutionsEditDialog
                    {
                        DataContext = new SubstitutionsViewModel(_groupsViewModel.Groups),
                        Owner = owner
                    };
                    substitutionsEditDialog.Closed += (s, e) =>
                    {
                        OnPropertyChanged("SubstitutionsCount");
                        SettingsHolder.Instance.SetProfile(_groupsViewModel.Name);
                    };
                    substitutionsEditDialog.Show();
                    break;
                case "Triggers":
                    var triggersViewModel = new TriggersViewModel(_groupsViewModel.Groups, RootModel.AllActionDescriptions);
                    triggersViewModel.TriggersChanged += (s, e) => RefreshTriggerCacheOnLiveTabs();
                    var triggerEditDialog = new TriggersEditDialog
                    {
                        DataContext = triggersViewModel,
                        Owner = owner
                    };
                    triggerEditDialog.Closed += (s, e) =>
                    {
                        OnPropertyChanged("TriggersCount");
                        SettingsHolder.Instance.SetProfile(_groupsViewModel.Name);
                        RefreshTriggerCacheOnLiveTabs();
                    };
                    triggerEditDialog.Show();
                    break;
            }
        }

        /// <summary>
        /// Adding/editing/removing a trigger replaces the TextTrigger instance inside
        /// the (live, shared -- ProfileHolder.Clone() only copies the Groups list, not
        /// the Group objects it holds) Group.Triggers list, but nothing on that path
        /// invalidates RootModel's cached EnabledTriggersOrderedByPriority snapshot --
        /// unlike EnableGroup/DisableGroup, which call RecalculatedEnabledTriggersPriorities()
        /// directly. SetProfile's ProfilesChanged event does eventually force a recalculation,
        /// but only for whichever RootModel happens to still hold a reference equal-by-name;
        /// relying on that round trip left edited triggers matching against their pre-edit
        /// pattern until something unrelated (toggling a group, reconnecting) forced a rebuild.
        /// Recalculate directly here instead, for every open tab using this profile.
        /// </summary>
        private void RefreshTriggerCacheOnLiveTabs()
        {
            if (_allRootModels == null)
                return;

            foreach (var rootModel in _allRootModels)
            {
                // Profile == null means this is the global groups editor (RootModel.Groups
                // pulls those in for every profile via Profile.Groups.Concat(GlobalGroups)),
                // so every open tab needs a refresh, not just ones on a matching profile.
                if (Profile == null || (rootModel.Profile != null && rootModel.Profile.Name == Profile.Name))
                {
                    rootModel.RecalculatedEnabledTriggersPriorities();
                }
            }
        }

        /// <summary>
        /// Pushes the Scripts dialog's edits (made against the CLONE that
        /// is this view model's <see cref="Profile"/>, see EditProfile's
        /// "Scripts" case) onto the live, SettingsHolder-tracked profile
        /// instance, persists it, and reloads it into every already-open
        /// tab using this profile. Called when profile settings are applied.
        /// Save button (dialog stays open) and its Closed event (window
        /// actually closing), so either path behaves identically.
        /// </summary>
        private void ApplyScriptsChanges()
        {
            OnPropertyChanged("ScriptsCount");

            SettingsHolder.Instance.GetProfile(Profile.Name).Scripts = Profile.Scripts;
            SettingsHolder.Instance.SetProfile(Profile.Name);

            if (_allRootModels != null)
            {
                foreach (var rootModel in _allRootModels)
                {
                    if (rootModel.Profile != null && rootModel.Profile.Name == Profile.Name)
                    {
                        rootModel.ReloadScripts();
                    }
                }
            }
        }

        private void ImportProfile(object obj)
        {
            OpenFileDialog fileDialog = new OpenFileDialog();
            fileDialog.DefaultExt = ".set";
            fileDialog.Filter = "Config|*.set|All Files|*.*";
            fileDialog.Multiselect = false;

            var result = fileDialog.ShowDialog();
            if (result.HasValue && result.Value)
            {
                if (!File.Exists(fileDialog.FileName))
                    return;

                RootModel rootModel = new RootModel(Profile);
                var conveyor = ConveyorFactory.CreateNew(rootModel);
                try
                {
                    using (var stream = new StreamReader(fileDialog.FileName, Encoding.Default, false, 1024))
                    {
                        string line;
                        while ((line = stream.ReadLine()) != null)
                        {
                            //XML не читает символ \x01B
                            //TODO: Need FIX IT
                            if (!line.Contains("\x001B"))
                            {
                                //rootModel.PushCommandToConveyor(new TextCommand(line)).ImportJMC(line, rootModel);
                                conveyor.ImportJMC(line, rootModel);
                            }
                        }
                    }
                }
                catch
                {
                }

                _groupsViewModel = new GroupsViewModel(Profile.Groups, Profile.Name, RootModel.AllActionDescriptions);
                var profile = SettingsHolder.Instance.GetProfile(Profile.Name);
                foreach(var newVar in Profile.Variables)
                {
                    var v = profile.Variables.FirstOrDefault(var => var.Name == newVar.Name);

                    if (v != null)
                    {
                        v.Value = newVar.Value;
                    }
                    else
                    {
                        profile.Variables.Add(new Variable() { Name = newVar.Name, Value = newVar.Value });
                    }
                }

                OnPropertyChanged("AliasesCount");
                OnPropertyChanged("GroupsCount");
                OnPropertyChanged("HighlightsCount");
                OnPropertyChanged("HotkeysCount");
                OnPropertyChanged("SubstitutionsCount");
                OnPropertyChanged("TriggersCount");
            }
        }
    }
}
