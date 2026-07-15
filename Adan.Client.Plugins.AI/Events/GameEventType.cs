namespace Adan.Client.Plugins.AI.Events
{
    public enum GameEventType
    {
        Unknown = 0,
        RoomEntered,
        RoomDescription,
        ExitDiscovered,
        MobSeen,
        MobKilled,
        ItemSeen,
        ItemPickedUp,
        ItemIdentified,
        CombatStarted,
        CombatEnded,
        PlayerDamaged,
        PlayerLowHealth,
        PlayerDied,
        QuestMessage,
        HiddenDoorFound,
        DoorOpened,
        ZoneChanged,
        PlayerMoved,
        UnknownImportantMessage,
    }
}
