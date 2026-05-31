// --------------------------------------------------------------------------------------------------------------------
// <copyright file="GroupMateViewModel.cs" company="Adamand MUD">
//   Copyright (c) Adamant MUD
// </copyright>
// <summary>
//   Defines the GroupMateViewModel type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Adan.Client.Plugins.GroupWidget.ViewModel
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Windows.Data;
    using Common.Model;
    using Common.Themes;
    using Common.ViewModel;

    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    /// <summary>
    /// View model for single group mate.
    /// </summary>
    public class GroupMateViewModel : ViewModelBase
    {
        private readonly List<AffectViewModel> _notProcessedAffects = new List<AffectViewModel>();
        private DateTime _last_timer_update = DateTime.Now;

        private bool _isDeleting;
        private TextColor _movesColor;
        private TextColor _hitsColor;
        private int _number;

        /// <summary>
        /// Initializes a new instance of the <see cref="GroupMateViewModel"/> class.
        /// </summary>
        public GroupMateViewModel([NotNull]CharacterStatus groupMate, [NotNull] IEnumerable<AffectDescription> affectsToDisplay, int number, double affectsPanelWidth)
        {
            Assert.ArgumentNotNull(groupMate, "groupMate");
            Assert.ArgumentNotNull(affectsToDisplay, "affectsToDisplay");

            _hitsColor = GetColor(groupMate.HitsPercent);
            _movesColor = GetColor(groupMate.MovesPercent);
            Affects = new List<AffectViewModel>(affectsToDisplay.Count());

            int priority = 0;
            foreach (var affectDescription in affectsToDisplay)
            {
                Affects.Add(new AffectViewModel(affectDescription, priority));
                priority++;
            }

            var affectsSortedAndFiltered = (ICollectionViewLiveShaping)CollectionViewSource.GetDefaultView(Affects);
            affectsSortedAndFiltered.IsLiveSorting = true;
            ((ICollectionView)affectsSortedAndFiltered).SortDescriptions.Add(new SortDescription() { Direction = ListSortDirection.Ascending, PropertyName = "Priority" });
            AffectsSortedAndFiltered = (ICollectionView)affectsSortedAndFiltered;
            AffectsPanelWidth = affectsPanelWidth;

            UpdateCharacter(groupMate, number);
        }

        /// <summary>
        /// 
        /// </summary>
        public CharacterStatus GroupMate
        {
            get;
            private set;
        }

        public double AffectsPanelWidth
        {
            get;
            private set;
        }

        public int Number
        {
            get
            {
                return _number;
            }
            private set
            {
                if (_number != value)
                {
                    _number = value;
                    OnPropertyChanged("Number");
                }
            }
        }

        public bool DisplayNumber { get; set; }

        /// <summary>
        /// Gets the name of group mate.
        /// </summary>
        [NotNull]
        public string Name
        {
            get;
            private set;
        }

        public TextColor TextColor
        {
            get { return TextColor.White; }
        }

        /// <summary>
        /// Gets the color to use to display hits.
        /// </summary>
        /// <value>
        /// <see cref="TextColor"/> to use to display hits.
        /// </value>
        public TextColor HitsColor
        {
            get
            {
                return _hitsColor;
            }

            private set
            {
                if (value != _hitsColor)
                {
                    _hitsColor = value;
                    OnPropertyChanged("HitsColor");
                }
            }
        }

        /// <summary>
        /// Gets the color to use to display moves.
        /// </summary>
        /// <value>
        /// <see cref="TextColor"/> to use to display moves.
        /// </value>
        public TextColor MovesColor
        {
            get
            {
                return _movesColor;
            }

            private set
            {
                if (value != _movesColor)
                {
                    _movesColor = value;
                    OnPropertyChanged("MovesColor");
                }
            }
        }

        /// <summary>
        /// Gets the group mate hits percent.
        /// </summary>
        private float _hits_percent;
        public float HitsPercent
        {
            get
            {
                return _hits_percent;
            }

            private set
            {
                if (value != _hits_percent)
                {
                    _hits_percent = value;
                    OnPropertyChanged("HitsPercent");
                }
            }
        }

        private int _lastServerMemTime;
        private float _mem_time;
        public string MemTime
        {
            get
            {
                if (!MemTimeVisibleSetting)
                {
                    return string.Empty;
                }

                if (_mem_time < 0)
                {
                    return "-----";
                }

                if (_mem_time == 0)
                {
                    return string.Empty;
                }

                var memTimeMinutes = (int)_mem_time / 60;
                var memTimeSeconds = (int)_mem_time % 60;
                return string.Format("{0:00}:{1:00}", memTimeMinutes, memTimeSeconds);
            }
        }

        private float _wait_state;
        public float WaitTimeHeight
        {
            get
            {
                if (_wait_state > 80)
                {
                    return 20;
                }

                if (_wait_state <= 0)
                {
                    return 0;
                }

                return _wait_state * 20.0f / 80.0f;
            }
        }

        private float _moves_percent;
        /// <summary>
        /// Gets the group mate moves percent.
        /// </summary>
        public float MovesPercent
        {
            get
            {
                return _moves_percent;
            }

            private set
            {
                if (value != _moves_percent)
                {
                    _moves_percent = value;
                    OnPropertyChanged("MovesPercent");
                }
            }
        }

        private Position _position;
        /// <summary>
        /// Gets the grou mate position.
        /// </summary>
        public Position Position
        {
            get
            {
                return _position;
            }

            private set
            {
                if (value != _position)
                {
                    _position = value;
                    OnPropertyChanged("Position");
                }
            }
        }

        private bool _is_attacked;
        /// <summary>
        /// Gets a value indicating whether this charrackter is attacked by somebody else.
        /// </summary>
        /// <value>
        /// <c>true</c> if this charackter is attacked; otherwise, <c>false</c>.
        /// </value>
        public bool IsAttacked
        {
            get
            {
                return _is_attacked;
            }

            private set
            {
                if (value != _is_attacked)
                {
                    _is_attacked = value;
                    OnPropertyChanged("IsAttacked");
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this character is in same room with current player.
        /// </summary>
        /// <value>
        /// <c>true</c> if this character is in same room; otherwise, <c>false</c>.
        /// </value>
        private bool _in_same_room;
        public bool InSameRoom
        {
            get
            {
                return _in_same_room;
            }

            set
            {
                if (value != _in_same_room)
                {
                    _in_same_room = value;
                    OnPropertyChanged("InSameRoom");
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this group mate is being deleted.
        /// </summary>
        /// <value>
        /// <c>true</c> if this group mate is being deleted.; otherwise, <c>false</c>.
        /// </value>
        public bool IsDeleting
        {
            get
            {
                return _isDeleting;
            }

            set
            {
                _isDeleting = value;
                OnPropertyChanged("IsDeleting");
            }
        }

        /// <summary>
        /// Gets the affects of this character.
        /// </summary>
        [NotNull]
        public IList<AffectViewModel> Affects
        {
            get;
            private set;
        }

        [NotNull]
        public ICollectionView AffectsSortedAndFiltered { get; private set; }

        public bool MemTimeVisibleSetting { get; set; }

        /// <summary>
        /// Updates this view model from model.
        /// </summary>
        public virtual void UpdateFromModel([NotNull] CharacterStatus characterStatus, int position)
        {
            Assert.ArgumentNotNull(characterStatus, "characterStatus");

            if (characterStatus.Name != Name)
            {
                return;
            }

            UpdateCharacter(characterStatus, position);
        }

        private void UpdateCharacter([NotNull] CharacterStatus characterStatus, int position)
        {
            Name = characterStatus.Name;

            HitsPercent = characterStatus.HitsPercent;

            MovesPercent = characterStatus.MovesPercent;
            
            Position = characterStatus.Position;
            IsAttacked = characterStatus.IsAttacked;
            InSameRoom = characterStatus.InSameRoom;
            Number = position;
            HitsColor = GetColor(HitsPercent);
            MovesColor = GetColor(MovesPercent);

            UpdateAffects(characterStatus);

            if (MemTimeVisibleSetting && _lastServerMemTime != characterStatus.MemTime)
            {
                _lastServerMemTime = characterStatus.MemTime;
                _mem_time = _lastServerMemTime;
                _last_timer_update = DateTime.Now;
                OnPropertyChanged("MemTime");
            }

            var oldWaitTime = _wait_state;
            _wait_state = characterStatus.WaitState;
            if (oldWaitTime != _wait_state)
            {
                OnPropertyChanged("WaitTimeHeight");
            }

            GroupMate = characterStatus;
        }

        private void UpdateAffects([NotNull] CharacterStatus characterStatus)
        {
            _notProcessedAffects.Clear();
            _notProcessedAffects.AddRange(Affects);

            foreach (var affect in characterStatus.Affects)
            {
                foreach (var affectViewModel in Affects)
                {
                    if (affectViewModel.AffectDescription.Matches(affect.Name))
                    {
                        affectViewModel.UpdateFromModel(affect);
                        _notProcessedAffects.Remove(affectViewModel);
                        break;
                    }
                }
            }

            foreach (var notProcessedAffect in _notProcessedAffects)
            {
                notProcessedAffect.OnAffectRemoved();
            }
        }

        /// <summary>
        /// Updates the timings.
        /// </summary>
        public void UpdateTimings(DateTime now)
        {
            foreach (var affectViewModel in Affects)
            {
                affectViewModel.UpdateTimings(now);
            }

            var old_mem_time = _mem_time;
            if (_mem_time > 0)
            {
                _mem_time -= (float)(now - _last_timer_update).TotalSeconds;
                if (_mem_time < 0)
                {
                    _mem_time = 0;
                }

                _last_timer_update = now;

                if ((old_mem_time / 60  != _mem_time / 60) || (old_mem_time % 60 != _mem_time % 60))
                {
                    OnPropertyChanged("MemTime");
                }
            }
        }

        private static TextColor GetColor(float hitsPercent)
        {
            if (hitsPercent > 80)
            {
                return TextColor.Green;
            }

            if (hitsPercent > 20)
            {
                return TextColor.Yellow;
            }

            return hitsPercent > 10 ? TextColor.Red : TextColor.BrightRed;
        }
    }
}
