// --------------------------------------------------------------------------------------------------------------------
// <copyright file="TriggerUnit.cs" company="Adamand MUD">
//   Copyright (c) Adamant MUD
// </copyright>
// <summary>
//   Defines the TriggerUnit type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using Adan.Client.Common.Conveyor;

namespace Adan.Client.ConveyorUnits
{
    using System.Collections.Generic;
    using System.Linq;
    using Common.ConveyorUnits;
    using Common.Messages;
    using CSLib.Net.Diagnostics;

    /// <summary>
    /// A <see cref="ConveyorUnit"/> that processes triggers.
    /// </summary>
    public class TriggerUnit : ConveyorUnit
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TriggerUnit"/> class.
        /// </summary>
        public TriggerUnit(MessageConveyor conveyor)
            : base(conveyor)
        {
        }

        #region Overrides of ConveyorUnit

        /// <summary>
        /// Gets a set of message types that this unit can handle.
        /// </summary>
        public override IEnumerable<int> HandledMessageTypes
        {
            get
            {
                return new[] { BuiltInMessageTypes.TextMessage };
            }
        }

        /// <summary>
        /// Gets a set of command types that this unit can handle.
        /// </summary>
        public override IEnumerable<int> HandledCommandTypes
        {
            get
            {
                return Enumerable.Empty<int>();
            }
        }
        
        public override void HandleMessage(Message message)
        {
            Assert.ArgumentNotNull(message, "message");
            var textMsg = message as Common.Messages.TextMessage;
            foreach (var trigger in Conveyor.RootModel.EnabledTriggersOrderedByPriority)
            {
                if (message.SkipTriggers)
                {
                    break;
                }

#if DEBUG
                var sw = System.Diagnostics.Stopwatch.StartNew();
                trigger.HandleMessage(message, Conveyor.RootModel);
                sw.Stop();
                if (sw.ElapsedMilliseconds >= 3)
                {
                    var pattern = trigger.GetPatternString();
                    if (pattern != null && pattern.Length > 60) pattern = pattern.Substring(0, 60) + "...";
                    var text = textMsg?.InnerText ?? "";
                    if (text.Length > 60) text = text.Substring(0, 60) + "...";
                    Common.Conveyor.PerfLog.Write(
                        string.Format("  Trigger[{0}]", pattern),
                        text,
                        sw.ElapsedMilliseconds);
                }
#else
                trigger.HandleMessage(message, Conveyor.RootModel);
#endif
            }
        }

        #endregion
    }
}
