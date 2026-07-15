namespace Adan.Client.Plugins.AI.Memory
{
    public class ExitRecord
    {
        public long Id { get; set; }
        public long FromRoomId { get; set; }
        public string Direction { get; set; }
        public long? ToRoomId { get; set; }
        public bool IsConfirmed { get; set; }
    }
}
