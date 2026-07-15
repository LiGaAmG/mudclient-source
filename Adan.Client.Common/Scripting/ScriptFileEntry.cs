using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Adan.Client.Common.Scripting
{
    public class ScriptFileEntry
    {
        public ScriptFileEntry()
        {
            EnabledProfileNames = new List<string>();
        }

        public string FileName { get; set; }

        public bool IsGlobal { get; set; }

        public bool AutoStart { get; set; }

        public List<string> EnabledProfileNames { get; set; }

        // Compatibility with the unfinished WinForms editor. Remove after its
        // per-tab controls have been replaced with profile assignment controls.
        public bool IsShared { get { return IsGlobal; } set { IsGlobal = value; } }
        public List<string> EnabledTabUids { get { return EnabledProfileNames; } set { EnabledProfileNames = value; } }
    }

    [DataContract]
    public class ScriptFileMetadata
    {
        [DataMember(Name = "version")]
        public int Version { get; set; }

        [DataMember(Name = "global")]
        public bool IsGlobal { get; set; }

        [DataMember(Name = "autoStart")]
        public bool AutoStart { get; set; }

        [DataMember(Name = "profiles")]
        public List<string> ProfileNames { get; set; }
    }
}
