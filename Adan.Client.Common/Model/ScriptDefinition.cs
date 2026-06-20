namespace Adan.Client.Common.Model
{
    using System;
    using System.Xml.Serialization;

    using CSLib.Net.Annotations;

    /// <summary>
    /// A single named, persisted Lua script not tied to any trigger/alias --
    /// the "global" scripts editable via the Scripts dialog. Mirrors
    /// Variable.cs's persistence shape (flat, profile-scoped, no Group nesting).
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

        /// <summary>
        /// Kept only so old Scripts.xml files (saved before
        /// HandleGroupState/HandleRoomState/HandleRoomChange existed) still
        /// deserialize and keep working -- see RootModel.ReloadScripts(),
        /// which treats this as equivalent to the matching bool being true
        /// if none of the three bools are set. Not written for newly
        /// edited scripts (the editor UI no longer has a single-choice
        /// Handler control; ScriptViewModel.HandlerKind setter is gone).
        /// </summary>
        [XmlAttribute]
        public ScriptHandlerKind HandlerKind
        {
            get;
            set;
        }

        /// <summary>
        /// If true, this script's code must define a function named
        /// exactly "on_group_state" -- see LuaScriptHost.RegisterGroupStateHandler.
        /// Independent of the other two Handle* flags: one script can
        /// define and register handlers for several packet types at once.
        /// </summary>
        [XmlAttribute]
        public bool HandleGroupState
        {
            get;
            set;
        }

        /// <summary>
        /// If true, this script's code must define a function named
        /// exactly "on_room_state" -- see LuaScriptHost.RegisterRoomStateHandler.
        /// </summary>
        [XmlAttribute]
        public bool HandleRoomState
        {
            get;
            set;
        }

        /// <summary>
        /// If true, this script's code must define a function named
        /// exactly "on_room_change" -- see LuaScriptHost.RegisterRoomChangeHandler.
        /// </summary>
        [XmlAttribute]
        public bool HandleRoomChange
        {
            get;
            set;
        }
    }
}
