namespace Adan.Client.Plugins.AI.Memory
{
    public class LoreChunkRecord
    {
        public long Id { get; set; }
        public string DocPath { get; set; }
        public string DocTitle { get; set; }
        public string SectionTitle { get; set; }
        public string Content { get; set; }
        public double Score { get; set; }
    }
}
