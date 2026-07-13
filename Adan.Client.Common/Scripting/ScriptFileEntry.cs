using System.Collections.Generic;
using System.Xml.Serialization;

namespace Adan.Client.Common.Scripting
{
    public class ScriptFileEntry
    {
        public ScriptFileEntry()
        {
            EnabledTabUids = new List<string>();
        }

        [XmlAttribute]
        public string FileName { get; set; }

        [XmlAttribute]
        public bool IsShared { get; set; }

        [XmlAttribute]
        public bool AutoStart { get; set; }

        [XmlArray]
        [XmlArrayItem("Uid")]
        public List<string> EnabledTabUids { get; set; }
    }

    [XmlRoot("ScriptFiles")]
    public class ScriptFileMetadata
    {
        public ScriptFileMetadata()
        {
            Entries = new List<ScriptFileEntry>();
        }

        [XmlElement("Script")]
        public List<ScriptFileEntry> Entries { get; set; }
    }
}
