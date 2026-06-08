namespace Adan.Client.Plugins.GroupWidget.ViewModel
{
    using System.Collections.Generic;

    using Common.Model;
    using Common.Themes;
    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    /// <summary>
    /// View model for single monster in the players room.
    /// </summary>
    public class MonsterViewModel : GroupMateViewModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MonsterViewModel"/> class.
        /// </summary>
        public MonsterViewModel([NotNull] MonsterStatus monsterStatus, [NotNull] IEnumerable<AffectDescription> affectsToDisplay, int number, double affectsPanelWidth)
            : base(monsterStatus, affectsToDisplay, number, affectsPanelWidth)
        {
            Assert.ArgumentNotNull(monsterStatus, "monsterStatus");
            Assert.ArgumentNotNull(affectsToDisplay, "affectsToDisplay");

            MonsterStatus = monsterStatus;

            _boss = monsterStatus.IsBoss;
            _player_character = monsterStatus.IsPlayerCharacter;
        }


        /// <summary>
        /// Gets the monster status.
        /// </summary>
        [NotNull]
        public MonsterStatus MonsterStatus
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether this monster is player character.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this monsters is player character; otherwise, <c>false</c>.
        /// </value>
        private bool _player_character;
        public bool PlayerCharacter
        {
            get
            {
                return _player_character;
            }

            set
            {
                if (value != _player_character)
                {
                    _player_character = value;
                    OnPropertyChanged("PlayerCharacter");
                }
            }
        }

        public TextColor PlayerCharacterColor
        {
            get
            {
                return TextColor.BrightRed;
            }
        }
        /// <summary>
        /// Gets or sets a value indicating whether this monster is boss.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this monster is boss; otherwise, <c>false</c>.
        /// </value>
        private bool _boss;
        public bool Boss
        {
            get
            {
                return _boss;
            }

            set
            {
                if (value != _boss)
                {
                    _boss = value;
                    OnPropertyChanged("Boss");
                }
            }
        }

        private int _monsterId;
        public int MonsterId
        {
            get { return _monsterId; }
            set
            {
                _monsterId = value;
                OnPropertyChanged("MonsterId");
                OnPropertyChanged("MonsterIdLabel");
            }
        }

        public string MonsterIdLabel
        {
            get { return "id" + _monsterId; }
        }

        /// <summary>
        /// Reassigns this VM to a completely different monster (pool reuse, bypasses name check).
        /// </summary>
        public void Reassign([NotNull] MonsterStatus monster, int position)
        {
            Assert.ArgumentNotNull(monster, "monster");
            MonsterStatus = monster;
            PlayerCharacter = monster.IsPlayerCharacter;
            Boss = monster.IsBoss;
            base.Reassign(monster, position);
        }

        /// <summary>
        /// Updates this view model from model.
        /// </summary>
        public override void UpdateFromModel(CharacterStatus characterStatus, int position)
        {
            Assert.ArgumentNotNull(characterStatus, "characterStatus");
            var monsterStatus = characterStatus as MonsterStatus;
            if (monsterStatus != null)
            {
                PlayerCharacter = monsterStatus.IsPlayerCharacter;
                Boss = monsterStatus.IsBoss;
                MonsterStatus = monsterStatus;
            }

            base.UpdateFromModel(characterStatus, position);
        }
    }
}
