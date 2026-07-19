namespace Adan.Client.Plugins.GroupWidget.ConveyorUnits
{
    using System.Collections.Generic;
    using System.Linq;
    using Common.Commands;
    using Common.Conveyor;
    using Common.ConveyorUnits;
    using Common.Messages;
    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;
    using Messages;

    /// <summary>
    /// <see cref="ConveyorUnit"/> implementation to handle <see cref="RoomMonstersMessage"/> messages.
    /// </summary>
    public class RoomMonstersUnit : ConveyorUnit
    {
        private readonly MonstersWidgetControl _monstersWidgetControl;

        public RoomMonstersUnit([NotNull] MonstersWidgetControl monstersWidgetControl, MessageConveyor conveyor)
            : base(conveyor)
        {
            Assert.ArgumentNotNull(monstersWidgetControl, "monstersWidgetControl");

            _monstersWidgetControl = monstersWidgetControl;
        }

        /// <summary>
        /// Gets a set of message types that this unit can handle.
        /// </summary>
        public override IEnumerable<int> HandledMessageTypes
        {
            get
            {
                return Enumerable.Empty<int>();
            }
        }

        /// <summary>
        /// Gets a set of command types that this unit can handle.
        /// </summary>
        public override IEnumerable<int> HandledCommandTypes
        {
            get
            {
                return Enumerable.Repeat(BuiltInCommandTypes.TextCommand, 1);
            }
        }
        
        public override void HandleCommand([NotNull]Command command, bool isImport = false)
        {
            Assert.ArgumentNotNull(command, "command");

            var textCommand = command as TextCommand;
            if (textCommand == null)
                return;

           if (textCommand.CommandText.StartsWith("#nextmonster", System.StringComparison.InvariantCulture))
           {
                textCommand.Handled = true;

                _monstersWidgetControl.NextMonster();
            }
            else if (textCommand.CommandText.StartsWith("#previousmonster", System.StringComparison.InvariantCulture))
            {
                textCommand.Handled = true;

                _monstersWidgetControl.PreviousMonster();
            }
            else if (textCommand.CommandText.StartsWith("#testmonster", System.StringComparison.InvariantCulture))
            {
                textCommand.Handled = true;

                var msg = new Messages.RoomMonstersMessage();
                msg.Monsters.Add(new Common.Model.MonsterStatus { Name = "Пигмей-маг" });
                Conveyor.PushMessage(msg);
            }
            else if (textCommand.CommandText.StartsWith("#debugeffects", System.StringComparison.InvariantCulture))
            {
                textCommand.Handled = true;
                ShowMonsterEffects();
            }
        }

        private void ShowMonsterEffects()
        {
            var monsters = _monstersWidgetControl.GetLastMonsters();
            if (monsters == null || monsters.Count == 0)
            {
                Conveyor.PushMessage(new InfoMessage("[#showeffects] нет данных о монстрах"));
                return;
            }

            Conveyor.PushMessage(new InfoMessage("[#showeffects] --- монстры ---"));
            foreach (var monster in monsters)
            {
                if (monster.Affects.Count == 0)
                {
                    Conveyor.PushMessage(new InfoMessage(string.Format("[{0}] нет эффектов", monster.Name)));
                }
                else
                {
                    foreach (var affect in monster.Affects)
                    {
                        Conveyor.PushMessage(new InfoMessage(string.Format(
                            "[{0}] '{1}' dur={2} rounds={3}",
                            monster.Name, affect.Name, affect.Duration, affect.Rounds)));
                    }
                }
            }
        }
    }
}
