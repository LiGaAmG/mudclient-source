using System.Data.SQLite;

namespace Adan.Client.Plugins.AI.Memory
{
    internal static class DbSchema
    {
        private const int CurrentVersion = 1;

        public static void EnsureSchema(SQLiteConnection conn)
        {
            int version = GetVersion(conn);
            if (version < 1)
                ApplyV1(conn);
            SetVersion(conn, CurrentVersion);
        }

        private static int GetVersion(SQLiteConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA user_version";
                return (int)(long)cmd.ExecuteScalar();
            }
        }

        private static void SetVersion(SQLiteConnection conn, int v)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA user_version = " + v;
                cmd.ExecuteNonQuery();
            }
        }

        private static void ApplyV1(SQLiteConnection conn)
        {
            // Р’С‹РїРѕР»РЅСЏРµРј РєР°Р¶РґС‹Р№ РѕРїРµСЂР°С‚РѕСЂ РѕС‚РґРµР»СЊРЅРѕ вЂ” SQLite РЅРµ РїРѕРґРґРµСЂР¶РёРІР°РµС‚ РЅРµСЃРєРѕР»СЊРєРѕ РѕРїРµСЂР°С‚РѕСЂРѕРІ РІ РѕРґРЅРѕРј ExecuteNonQuery РЅР°РґС‘Р¶РЅРѕ
            string[] statements = new[]
            {
                "PRAGMA journal_mode=WAL",
                "PRAGMA foreign_keys=ON",
                @"CREATE TABLE IF NOT EXISTS Zones (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    NormalizedName TEXT NOT NULL,
                    FirstSeenAt TEXT NOT NULL,
                    LastSeenAt TEXT NOT NULL,
                    Summary TEXT
                )",
                "CREATE UNIQUE INDEX IF NOT EXISTS ix_zones_norm ON Zones(NormalizedName)",
                @"CREATE TABLE IF NOT EXISTS Rooms (
                    Id INTEGER PRIMARY KEY,
                    ZoneId INTEGER NOT NULL REFERENCES Zones(Id),
                    Name TEXT NOT NULL,
                    NormalizedNameHash TEXT NOT NULL,
                    Description TEXT,
                    FirstSeenAt TEXT NOT NULL,
                    LastSeenAt TEXT NOT NULL,
                    VisitCount INTEGER NOT NULL DEFAULT 1,
                    Notes TEXT
                )",
                "CREATE INDEX IF NOT EXISTS ix_rooms_zone ON Rooms(ZoneId)",
                "CREATE INDEX IF NOT EXISTS ix_rooms_hash ON Rooms(ZoneId, NormalizedNameHash)",
                @"CREATE TABLE IF NOT EXISTS RoomExits (
                    Id INTEGER PRIMARY KEY,
                    FromRoomId INTEGER NOT NULL REFERENCES Rooms(Id),
                    Direction TEXT NOT NULL,
                    ToRoomId INTEGER REFERENCES Rooms(Id),
                    IsConfirmed INTEGER NOT NULL DEFAULT 0,
                    FirstSeenAt TEXT NOT NULL,
                    LastSeenAt TEXT NOT NULL
                )",
                "CREATE UNIQUE INDEX IF NOT EXISTS ix_exits_uniq ON RoomExits(FromRoomId, Direction)",
                @"CREATE TABLE IF NOT EXISTS Mobs (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    NormalizedName TEXT NOT NULL,
                    Description TEXT,
                    FirstSeenAt TEXT NOT NULL,
                    LastSeenAt TEXT NOT NULL
                )",
                "CREATE UNIQUE INDEX IF NOT EXISTS ix_mobs_norm ON Mobs(NormalizedName)",
                @"CREATE TABLE IF NOT EXISTS Items (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT NOT NULL,
                    NormalizedName TEXT NOT NULL,
                    Description TEXT,
                    FirstSeenAt TEXT NOT NULL,
                    LastSeenAt TEXT NOT NULL
                )",
                "CREATE UNIQUE INDEX IF NOT EXISTS ix_items_norm ON Items(NormalizedName)",
                @"CREATE TABLE IF NOT EXISTS RoomMobs (
                    RoomId INTEGER NOT NULL REFERENCES Rooms(Id),
                    MobId INTEGER NOT NULL REFERENCES Mobs(Id),
                    LastSeenAt TEXT NOT NULL,
                    PRIMARY KEY(RoomId, MobId)
                )",
                @"CREATE TABLE IF NOT EXISTS RoomItems (
                    RoomId INTEGER NOT NULL REFERENCES Rooms(Id),
                    ItemId INTEGER NOT NULL REFERENCES Items(Id),
                    LastSeenAt TEXT NOT NULL,
                    PRIMARY KEY(RoomId, ItemId)
                )",
                @"CREATE TABLE IF NOT EXISTS GameEvents (
                    Id INTEGER PRIMARY KEY,
                    Timestamp TEXT NOT NULL,
                    EventType INTEGER NOT NULL,
                    ZoneId INTEGER REFERENCES Zones(Id),
                    RoomId INTEGER REFERENCES Rooms(Id),
                    RawText TEXT,
                    StructuredDataJson TEXT,
                    Importance INTEGER NOT NULL DEFAULT 1
                )",
                "CREATE INDEX IF NOT EXISTS ix_events_zone ON GameEvents(ZoneId, Timestamp)",
                @"CREATE TABLE IF NOT EXISTS UserNotes (
                    Id INTEGER PRIMARY KEY,
                    CreatedAt TEXT NOT NULL,
                    Text TEXT NOT NULL,
                    ZoneId INTEGER REFERENCES Zones(Id),
                    RoomId INTEGER REFERENCES Rooms(Id)
                )",
                @"CREATE TABLE IF NOT EXISTS LoreDocuments (
                    Path TEXT PRIMARY KEY,
                    Title TEXT,
                    ContentHash INTEGER NOT NULL,
                    IndexedAt TEXT NOT NULL
                )",

                @"CREATE TABLE IF NOT EXISTS LoreChunks (
                    Id INTEGER PRIMARY KEY,
                    DocPath TEXT NOT NULL,
                    ChunkIndex INTEGER NOT NULL,
                    SectionTitle TEXT,
                    Content TEXT NOT NULL
                )",
                "CREATE INDEX IF NOT EXISTS ix_chunks_doc ON LoreChunks(DocPath)"
            };

            foreach (var sql in statements)
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
            }

            // FTS5 is optional — not compiled in all SQLite distributions (e.g. NuGet System.Data.SQLite)
            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"CREATE VIRTUAL TABLE IF NOT EXISTS LoreChunksFts USING fts5(
                        DocPath UNINDEXED,
                        DocTitle,
                        SectionTitle,
                        Content,
                        tokenize='unicode61'
                    )";
                    cmd.ExecuteNonQuery();
                }
            }
            catch
            {
                // FTS5 not available; full-text search will be disabled
            }
        }
    }
}
