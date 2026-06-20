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

        [XmlAttribute]
        public ScriptHandlerKind HandlerKind
        {
            get;
            set;
        }
    }
}
