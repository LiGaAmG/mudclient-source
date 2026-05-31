namespace Adan.Client.Plugins.GroupWidget.ViewModel
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Windows.Threading;

    using Common.Model;
    using Common.ViewModel;

    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    using Properties;

    /// <summary>
    /// A view model for monsters status widget.
    /// </summary>
    public class RoomMonstersViewModel : ViewModelBase
    {
        private readonly DispatcherTimer _tickingTimer;

        private IList<string> _displayedAffectPriorities = new List<string>(Settings.Default.MonsterAffects);
        private int _displayedAffectCount = Settings.Default.MonsterDisplayAffectsCount;
        private bool _displayNumber = Settings.Default.MonsterDisplayNumber;
        private MonsterViewModel _selectedMonster;
        private bool _moreItemsAvailable;
        private int _nextMonsterId = 1;

        /// <summary>
        /// Initializes a new instance of the <see cref="RoomMonstersViewModel"/> class.
        /// </summary>
        public RoomMonstersViewModel()
        {
            Monsters = new ObservableCollection<MonsterViewModel>();

            _tickingTimer = new DispatcherTimer(DispatcherPriority.Background);
            _tickingTimer.Interval = TimeSpan.FromSeconds(1);
            _tickingTimer.Tick += (o, e) => UpdateTimings();
            _tickingTimer.Start();
            AffectsPanelWidth = _displayedAffectCount * 23;
            Width = AffectsPanelWidth + 22 + 30 + 140 + 60 + 20 + 20 + 5 + 5;
            if (!DisplayNumber)
            {
                Width -= 22;
            }

            DisplayedItemLimit = Settings.Default.IsMonsterLimitOn ? Settings.Default.MonsterLimit : 9999;
            if (DisplayedItemLimit < 1)
            {
                DisplayedItemLimit = 1;
            }
        }

        /// <summary>
        /// Gets the Affects panel width.
        /// </summary>
        public double AffectsPanelWidth
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the width of control displaying monsters.
        /// </summary>
        public double Width
        {
            get; private set;
        }

        public RootModel RootModel
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the selected monster.
        /// </summary>
        /// <value>
        /// The selected monster.
        /// </value>
        [CanBeNull]
        public MonsterViewModel SelectedMonster
        {
            get
            {
                return _selectedMonster;
            }

            set
            {
                _selectedMonster = value;
                if (RootModel != null)
                    RootModel.SelectedRoomMonster = value != null ? value.MonsterStatus : null;
                OnPropertyChanged("SelectedMonster");
            }
        }

        /// <summary>
        /// Gets the group mates of current player.
        /// </summary>
        [NotNull]
        public ObservableCollection<MonsterViewModel> Monsters
        {
            get;
            private set;
        }

        public int DisplayedAffectCount
        {
            get { return _displayedAffectCount; }
            set
            {
                _displayedAffectCount = value;
                OnPropertyChanged("DisplayedAffectCount");
            }
        }

        public bool DisplayNumber
        {
            get { return _displayNumber; }
            set
            {
                _displayNumber = value;
                OnPropertyChanged("DisplayNumber");
            }
        }

        public bool MoreItemsAvailable
        {
            get { return _moreItemsAvailable; }
            set
            {
                if (_moreItemsAvailable != value)
                {
                    _moreItemsAvailable = value;
                    OnPropertyChanged("MoreItemsAvailable");
                }
            }
        }

        public int DisplayedItemLimit { get; set; }

        /// <summary>
        /// Updates the model.
        /// </summary>
        /// <param name="monsters">The monsters.</param>
        public void UpdateModel([NotNull] IEnumerable<MonsterStatus> monsters, bool isRound = true)
        {
            Assert.ArgumentNotNull(monsters, "monsters");

            var monsterList = monsters.ToList();
            if (monsterList.Count == 0 || !isRound)
            {
                _nextMonsterId = 1;
                Monsters.Clear();
                MoreItemsAvailable = false;
                if (monsterList.Count == 0)
                    return;
            }

            // Group existing VMs by name for HP-based matching
            var existingByName = new Dictionary<string, List<MonsterViewModel>>(StringComparer.OrdinalIgnoreCase);
            foreach (var existing in Monsters)
            {
                List<MonsterViewModel> list;
                if (!existingByName.TryGetValue(existing.Name, out list))
                {
                    list = new List<MonsterViewModel>();
                    existingByName[existing.Name] = list;
                }
                list.Add(existing);
            }

            // Group new monsters by name for HP-based matching
            var newByName = new Dictionary<string, List<MonsterStatus>>(StringComparer.OrdinalIgnoreCase);
            foreach (var monster in monsterList)
            {
                List<MonsterStatus> list;
                if (!newByName.TryGetValue(monster.Name, out list))
                {
                    list = new List<MonsterStatus>();
                    newByName[monster.Name] = list;
                }
                list.Add(monster);
            }

            // Build ID mapping per same-name group:
            // - New monsters always arrive at the FRONT of the list
            // - So when count grew: existing ones are at the TAIL → tail-match
            // - When count shrank (deaths): use HP matching to find survivors
            var vmForMonster = new Dictionary<MonsterStatus, MonsterViewModel>();
            foreach (var name in existingByName.Keys)
            {
                List<MonsterStatus> newGroup;
                if (!newByName.TryGetValue(name, out newGroup) || newGroup.Count == 0)
                    continue;

                var oldGroup = existingByName[name];

                if (newGroup.Count >= oldGroup.Count)
                {
                    // Arrivals: new ones at front, existing ones at tail
                    int offset = newGroup.Count - oldGroup.Count;
                    for (int i = 0; i < oldGroup.Count; i++)
                        vmForMonster[newGroup[offset + i]] = oldGroup[i];
                }
                else
                {
                    // Deaths/departures: find minimum-cost HP matching (optimal, not greedy)
                    var oldHps = oldGroup.Select(vm => vm.MonsterStatus.HitsPercent).ToList();
                    var newHps = newGroup.Select(m => m.HitsPercent).ToList();
                    var assignment = MinCostAssignment(oldHps, newHps);
                    for (int i = 0; i < newGroup.Count; i++)
                        vmForMonster[newGroup[i]] = oldGroup[assignment[i]];
                }
            }

            var newList = new List<MonsterViewModel>();
            bool moreItemsAvailable = false;
            int position = 1;
            foreach (var monster in monsterList)
            {
                if (position > DisplayedItemLimit)
                {
                    moreItemsAvailable = true;
                    break;
                }

                MonsterViewModel vm;
                if (vmForMonster.TryGetValue(monster, out vm))
                {
                    vm.UpdateFromModel(monster, position);
                    if (SelectedMonster != null && SelectedMonster == vm && RootModel != null)
                        RootModel.SelectedRoomMonster = monster;
                }
                else
                {
                    var affectsList = _displayedAffectPriorities.Select(af => Constants.AllAffects.First(a => a.Name == af));
                    vm = new MonsterViewModel(monster, affectsList, position, AffectsPanelWidth) { DisplayNumber = DisplayNumber };
                    vm.MonsterId = _nextMonsterId++;
                }

                newList.Add(vm);
                position++;
            }

            // Sync Monsters collection: update in-place to preserve selection and avoid flicker
            for (int i = 0; i < newList.Count; i++)
            {
                if (i < Monsters.Count)
                {
                    if (Monsters[i] != newList[i])
                        Monsters[i] = newList[i];
                }
                else
                {
                    Monsters.Add(newList[i]);
                }
            }

            while (Monsters.Count > newList.Count)
            {
                if (SelectedMonster == Monsters[Monsters.Count - 1])
                    SelectedMonster = null;
                Monsters.RemoveAt(Monsters.Count - 1);
            }

            MoreItemsAvailable = moreItemsAvailable;

            // Update MonsterIdMap so $monsteridN variables resolve correctly
            if (RootModel != null)
            {
                RootModel.MonsterIdMap.Clear();
                foreach (var vm in newList)
                {
                    if (vm.MonsterId > 0)
                        RootModel.MonsterIdMap[vm.MonsterId] = vm.MonsterStatus;
                }
            }
        }

        /// <summary>
        /// Reloads the displayed affects.
        /// </summary>
        public void ReloadDisplayedAffects()
        {
            Monsters.Clear();
            _nextMonsterId = 1;
            _displayedAffectPriorities = new List<string>(Settings.Default.MonsterAffects);
            DisplayNumber = Settings.Default.GroupWidgetDisplayNumber;
            DisplayedAffectCount = Settings.Default.MonsterDisplayAffectsCount;
            AffectsPanelWidth = 23 * _displayedAffectCount;
            Width = AffectsPanelWidth + 22 + 30 + 140 + 60 + 20 + 20 + 5 + 5;
            if (!DisplayNumber)
            {
                Width -= 22;
            }

            DisplayedItemLimit = Settings.Default.IsMonsterLimitOn ? Settings.Default.MonsterLimit : 9999;
            if (DisplayedItemLimit < 1)
            {
                DisplayedItemLimit = 1;
            }
            MoreItemsAvailable = false;
            OnPropertyChanged("Width");
        }

        // Finds minimum-cost assignment: newHps[i] → oldHps[result[i]]
        // Brute-force, works correctly for N≤6 (typical monster group size)
        private static int[] MinCostAssignment(IList<float> oldHps, IList<float> newHps)
        {
            int n = newHps.Count;
            int m = oldHps.Count;
            var best = new int[n];
            for (int i = 0; i < n; i++) best[i] = i;
            float bestCost = float.MaxValue;
            var cur = new int[n];
            var used = new bool[m];

            Assign(0, 0f);

            return best;

            void Assign(int idx, float cost)
            {
                if (cost >= bestCost) return;
                if (idx == n)
                {
                    bestCost = cost;
                    System.Array.Copy(cur, best, n);
                    return;
                }
                for (int i = 0; i < m; i++)
                {
                    if (used[i]) continue;
                    used[i] = true;
                    cur[idx] = i;
                    Assign(idx + 1, cost + Math.Abs(newHps[idx] - oldHps[i]));
                    used[i] = false;
                }
            }
        }

        private void UpdateTimings()
        {
            foreach (var monster in Monsters)
            {
                monster.UpdateTimings(DateTime.Now);
            }
        }
    }
}

