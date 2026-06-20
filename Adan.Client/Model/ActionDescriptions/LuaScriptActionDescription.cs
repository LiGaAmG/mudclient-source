// --------------------------------------------------------------------------------------------------------------------
// <copyright file="LuaScriptActionDescription.cs" company="Adamand MUD">
//   Copyright (c) Adamant MUD
// </copyright>
// <summary>
//   Defines the LuaScriptActionDescription type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Adan.Client.Model.ActionDescriptions
{
    using System.Collections.Generic;

    using Actions;

    using Common.Model;
    using Common.Plugins;
    using Common.ViewModel;

    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    using ViewModel.Actions;

    /// <summary>
    /// Description of <see cref="LuaScriptAction"/>.
    /// </summary>
    public class LuaScriptActionDescription : ActionDescription
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LuaScriptActionDescription"/> class.
        /// </summary>
        /// <param name="allDescriptions">All descriptions.</param>
        public LuaScriptActionDescription([NotNull] IEnumerable<ActionDescription> allDescriptions)
            : base("Run Lua script", allDescriptions)
        {
            Assert.ArgumentNotNull(allDescriptions, "allDescriptions");
        }

        /// <summary>
        /// Creates the <see cref="ActionBase"/> derived class instance.
        /// </summary>
        /// <returns>The instance of <see cref="ActionBase"/> derived class.</returns>
        public override ActionBase CreateAction()
        {
            return new LuaScriptAction();
        }

        /// <summary>
        /// Creates the action view model by specified <see cref="ActionBase"/> instance.
        /// </summary>
        /// <param name="action">The <see cref="ActionBase"/> instance to create view model for.</param>
        /// <returns>Created action view model or <c>null</c> if specified action is not supported by this description.</returns>
        public override ActionViewModelBase CreateActionViewModel(ActionBase action)
        {
            Assert.ArgumentNotNull(action, "action");
            var luaAction = action as LuaScriptAction;
            if (luaAction != null)
            {
                return new LuaScriptActionViewModel(luaAction, this, AllDescriptions);
            }

            return null;
        }
    }
}
