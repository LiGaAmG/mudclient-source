using System.Collections.Generic;

namespace Adan.Client.Plugins.SpellManager
{
    public class SpellPresetEntry
    {
        public string SpellName { get; set; }
        public int Desired { get; set; }
        public int Priority { get; set; }
        public bool IsTrackedInCounter { get; set; }
        public bool IsTrackedGlobally { get; set; }
    }

    public class SpellPreset
    {
        public string Name { get; set; }
        public List<SpellPresetEntry> Entries { get; set; }

        public SpellPreset()
        {
            Entries = new List<SpellPresetEntry>();
        }
    }
}
