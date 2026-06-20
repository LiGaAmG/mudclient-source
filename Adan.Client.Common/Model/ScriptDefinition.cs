namespace Adan.Client.Common.Model
{
    using System;
    using System.Xml.Serialization;

    using CSLib.Net.Annotations;

    /// <summary>
    /// A single named, persisted Lua coroutine script not tied to any
    /// trigger/alias -- editable via the Scripts dialog. IsEnabled means
    /// "start automatically when a tab connects" (RootModel.ReloadScripts);
    /// it does not mean "currently running" -- see LuaScriptHost.GetScriptStatus
    /// for runtime state, which isn't persisted.
    /// </summary>
    [Serializable]
    public class ScriptDefinition
    {
        public ScriptDefinition()
        {
            Name = string.Empty;
            Code = string.Empty;
        }

        [NotNull]
        [XmlAttribute]
        public string Name
        {
            get;
            set;
        }

        [NotNull]
        [XmlElement]
        public string Code
        {
            get;
            set;
        }

        [XmlAttribute]
        public bool IsEnabled
        {
            get;
            set;
        }
    }
}
