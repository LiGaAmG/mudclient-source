// --------------------------------------------------------------------------------------------------------------------
// <copyright file="LuaScriptActionViewModel.cs" company="Adamand MUD">
//   Copyright (c) Adamant MUD
// </copyright>
// <summary>
//   Defines the LuaScriptActionViewModel type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Adan.Client.ViewModel.Actions
{
    using System.Collections.Generic;

    using Common.Plugins;
    using Common.ViewModel;

    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    using Model.Actions;

    /// <summary>
    /// View model for run Lua script action.
    /// </summary>
    public class LuaScriptActionViewModel : ActionViewModelBase
    {
        private readonly LuaScriptAction _action;

        /// <summary>
        /// Initializes a new instance of the <see cref="LuaScriptActionViewModel"/> class.
        /// </summary>
        /// <param name="action">The action.</param>
        /// <param name="actionDescriptor">The action descriptor.</param>
        /// <param name="allActionDescriptions">All action descriptions.</param>
        public LuaScriptActionViewModel(
            [NotNull] LuaScriptAction action,
            [NotNull] ActionDescription actionDescriptor,
            [NotNull] IEnumerable<ActionDescription> allActionDescriptions)
            : base(action, actionDescriptor, allActionDescriptions)
        {
            Assert.ArgumentNotNull(action, "action");
            Assert.ArgumentNotNull(actionDescriptor, "actionDescriptor");
            Assert.ArgumentNotNull(allActionDescriptions, "allActionDescriptions");

            _action = action;
        }

        /// <summary>
        /// Gets or sets the Lua source to run.
        /// </summary>
        [NotNull]
        public string ScriptText
        {
            get
            {
                return _action.ScriptText;
            }

            set
            {
                Assert.ArgumentNotNull(value, "value");

                _action.ScriptText = value;
                OnPropertyChanged("ScriptText");
                OnPropertyChanged("ActionDescription");
            }
        }

        /// <summary>
        /// Gets the action description.
        /// </summary>
        public override string ActionDescription
        {
            get { return "Lua: " + ScriptText; }
        }

        /// <summary>
        /// Clones this instance.
        /// </summary>
        /// <returns>A deep copy of this instance.</returns>
        public override ActionViewModelBase Clone()
        {
            return new LuaScriptActionViewModel(new LuaScriptAction(), ActionDescriptor, AllActionDescriptions)
            {
                ScriptText = ScriptText
            };
        }
    }
}
