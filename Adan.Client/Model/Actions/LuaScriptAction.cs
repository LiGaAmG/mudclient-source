// --------------------------------------------------------------------------------------------------------------------
// <copyright file="LuaScriptAction.cs" company="Adamand MUD">
//   Copyright (c) Adamant MUD
// </copyright>
// <summary>
//   Defines the LuaScriptAction type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Adan.Client.Model.Actions
{
    using System;
    using System.Xml.Serialization;

    using Common.Messages;
    using Common.Model;
    using Common.Scripting;

    using CSLib.Net.Annotations;
    using CSLib.Net.Diagnostics;

    /// <summary>
    /// Action that runs a Lua script in the tab's persistent LuaScriptHost
    /// when the owning trigger/alias fires. Parameter-less, like
    /// ClearVariableValueAction, so it derives from ActionBase directly
    /// rather than ActionWithParameters.
    /// </summary>
    [Serializable]
    public class LuaScriptAction : ActionBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LuaScriptAction"/> class.
        /// </summary>
        public LuaScriptAction()
        {
            ScriptText = string.Empty;
        }

        /// <summary>
        ///
        /// </summary>
        public override bool IsGlobal
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets or sets the Lua source to run.
        /// </summary>
        [NotNull]
        [XmlAttribute]
        public string ScriptText
        {
            get;
            set;
        }

        /// <summary>
        /// Executes this action.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <param name="context">The context.</param>
        public override void Execute(RootModel model, ActionExecutionContext context)
        {
            Assert.ArgumentNotNull(model, "model");
            Assert.ArgumentNotNull(context, "context");

            try
            {
                model.ScriptHost.LoadScript(ScriptText);
            }
            catch (LuaScriptTimeoutException ex)
            {
                model.PushMessageToConveyor(new ErrorMessage("Lua: " + ex.Message));
            }
            catch (Exception ex)
            {
                model.PushMessageToConveyor(new ErrorMessage("Lua: " + ex.Message));
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return "Lua: " + ScriptText;
        }
    }
}
