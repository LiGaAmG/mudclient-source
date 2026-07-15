# Local AI Plugin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Встроить локальный AI-помощник в MUD-клиент Adan без интернета, OpenAI и GPU — через LLamaSharp сайдкар-процесс и SQLite-память.

**Architecture:** Плагин `.NET 4.8` (`Adan.Client.Plugins.AI`) перехватывает команды и текст через `ConveyorUnit`. Инференс вынесен в отдельный `Adan.AI.Host.exe` (.NET 8) — он запускает GGUF через LLamaSharp и общается с плагином через именованный пайп (JSON-строки). Память хранится в SQLite через `System.Data.SQLite` (совместим с .NET 4.8). Настройки сериализуются в XML как и весь проект.

**Tech Stack:** C# .NET 4.8 (плагин) + .NET 8 (сайдкар), LLamaSharp 0.20.x + LLamaSharp.Backend.Cpu, System.Data.SQLite 1.0.118, NUnit 3.14, WPF, MEF, именованные пайпы (`System.IO.Pipes`)

**Key .NET 4.8 constraints:**
- `BlockingCollection<T>` вместо `Channel<T>`
- `System.Data.SQLite` вместо `Microsoft.Data.Sqlite`
- `Dispatcher.BeginInvoke` для UI-потока
- `Task.Run` + async/await (доступны в .NET 4.8)
- Настройки через XML-сериализацию как в остальных плагинах

---

## File Map

```
Adan.AI.Host/                          ← новый проект .NET 8 Console
  Adan.AI.Host.csproj
  Program.cs                           ← точка входа, именованный пайп
  LlmEngine.cs                         ← LLamaSharp обёртка
  PipeProtocol.cs                      ← модели запрос/ответ (shared-equivalent)
  HostSettings.cs                      ← парсинг аргументов командной строки

Adan.Client.Plugins.AI/               ← существующий пустой проект (переделать в SDK-style)
  Adan.Client.Plugins.AI.csproj        ← обновить на SDK-style + PackageReference
  AiPlugin.cs                          ← [Export(typeof(PluginBase))]

  Abstractions/
    ILocalLlmService.cs
    IGameMemoryService.cs
    ILoreSearchService.cs
    IAiContextBuilder.cs
    IAiCommentaryService.cs
    IAiCommandHandler.cs

  Configuration/
    AiSettings.cs                      ← XML-сериализуемые настройки
    AiSettingsSerializer.cs            ← загрузка/сохранение через SettingsHolder

  Inference/
    LocalLlmService.cs                 ← управляет сайдкар-процессом
    PipeProtocol.cs                    ← копия моделей для связи
    LlmRequest.cs
    LlmResponse.cs

  Memory/
    GameMemoryService.cs               ← все операции с SQLite
    DbSchema.cs                        ← CREATE TABLE + PRAGMA user_version миграции
    GameSessionState.cs                ← in-process текущее состояние (зона/комната/мобы)

  Events/
    GameEventExtractor.cs              ← перехват TextMessage → GameEvent
    GameEvent.cs
    GameEventType.cs
    IGameEventRule.cs
    Rules/
      RoomEnteredRule.cs
      MobSeenRule.cs
      MobKilledRule.cs
      ItemPickedUpRule.cs
      MovementRule.cs

  Lore/
    LoreIndexer.cs                     ← рекурсивный обход папки, чанки, FTS5
    LoreSearchService.cs

  Context/
    AiContextBuilder.cs

  Commentary/
    AiCommentaryService.cs

  Commands/
    AiCommandHandler.cs                ← /ai <cmd> парсинг и диспетчер
    AiConveyorUnit.cs                  ← ConveyorUnit: перехват TextCommand + TextMessage

  UI/
    AiSettingsControl.xaml
    AiSettingsControl.xaml.cs
    AiStatusViewModel.cs

Adan.Client.Plugins.AI.Tests/         ← новый тест-проект SDK-style net48 NUnit
  Adan.Client.Plugins.AI.Tests.csproj
  Events/
    GameEventExtractorTests.cs
  Memory/
    GameMemoryServiceTests.cs
  Lore/
    LoreIndexerTests.cs
  Commands/
    AiCommandHandlerTests.cs
  Context/
    AiContextBuilderTests.cs
  Fakes/
    FakeLlmService.cs

docs/local-ai.md
```

---

## ЭТАП A: Скелет, интерфейсы, настройки, SQLite

### Task A1: Обновить Adan.Client.Plugins.AI.csproj на SDK-style

**Files:**
- Modify: `Adan.Client.Plugins.AI/Adan.Client.Plugins.AI.csproj`

- [ ] Заменить содержимое .csproj:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <AssemblyName>Adan.Client.Plugins.AI</AssemblyName>
    <OutputType>Library</OutputType>
    <Nullable>disable</Nullable>
    <PlatformTarget>x86</PlatformTarget>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Data.SQLite" Version="1.0.118.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Adan.Client.Common\Adan.Client.Common.csproj" />
  </ItemGroup>
  <ItemGroup>
    <!-- WPF references для UI-контрола -->
    <Reference Include="PresentationCore" />
    <Reference Include="PresentationFramework" />
    <Reference Include="WindowsBase" />
    <Reference Include="System.ComponentModel.Composition" />
    <Reference Include="System.Xaml" />
  </ItemGroup>
</Project>
```

- [ ] Создать папки структуры:
```
mkdir Abstractions Configuration Inference Memory Events Events\Rules Lore Context Commentary Commands UI
```

- [ ] Собрать: `msbuild Adan.Client.Plugins.AI\Adan.Client.Plugins.AI.csproj`
- [ ] Убедиться что сборка проходит (пока пустой проект)

---

### Task A2: Интерфейсы

**Files:**
- Create: `Adan.Client.Plugins.AI/Abstractions/ILocalLlmService.cs`
- Create: `Adan.Client.Plugins.AI/Abstractions/IGameMemoryService.cs`
- Create: `Adan.Client.Plugins.AI/Abstractions/ILoreSearchService.cs`
- Create: `Adan.Client.Plugins.AI/Abstractions/IAiContextBuilder.cs`
- Create: `Adan.Client.Plugins.AI/Abstractions/IAiCommentaryService.cs`
- Create: `Adan.Client.Plugins.AI/Abstractions/IAiCommandHandler.cs`

- [ ] Создать `Abstractions/ILocalLlmService.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Adan.Client.Plugins.AI.Abstractions
{
    public enum LlmStatus { Disabled, ModelNotFound, Loading, Ready, Generating, Error }

    public interface ILocalLlmService : IDisposable
    {
        LlmStatus Status { get; }
        event EventHandler<LlmStatus> StatusChanged;
        Task LoadModelAsync(CancellationToken ct = default);
        void UnloadModel();
        Task<string> GenerateAsync(string prompt, CancellationToken ct = default);
        void CancelCurrent();
    }
}
```

- [ ] Создать `Abstractions/IGameMemoryService.cs`:

```csharp
using System;
using System.Collections.Generic;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Abstractions
{
    public interface IGameMemoryService : IDisposable
    {
        void Initialize();
        // Rooms & zones
        long UpsertZone(string name);
        long UpsertRoom(long zoneId, string name, string description);
        void ConfirmExit(long fromRoomId, string direction, long toRoomId);
        IList<RoomRecord> GetRoomsInZone(long zoneId);
        IList<ExitRecord> GetExitsFromRoom(long roomId);
        IList<long> FindShortestPath(long fromRoomId, long toRoomId);
        // Mobs & items
        long UpsertMob(string name, string description = null);
        long UpsertItem(string name, string description = null);
        void RecordMobInRoom(long roomId, long mobId);
        void RecordItemInRoom(long roomId, long itemId);
        // Events
        void SaveEvent(GameEventRecord ev);
        IList<GameEventRecord> GetRecentEvents(long? zoneId, int limit = 20);
        // Notes
        void SaveUserNote(string text, long? zoneId, long? roomId);
        // Zone summary
        string GetZoneSummary(long zoneId);
        void SaveZoneSummary(long zoneId, string summary);
        // Lore
        void SaveLoreDocument(string path, string title, long contentHash);
        bool LoreDocumentChanged(string path, long contentHash);
        void SaveLoreChunk(string docPath, int chunkIndex, string content, string sectionTitle);
        IList<LoreChunkRecord> SearchLore(string query, int limit = 5);
    }
}
```

- [ ] Создать `Abstractions/ILoreSearchService.cs`:

```csharp
using System.Collections.Generic;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Abstractions
{
    public interface ILoreSearchService
    {
        void ReindexAll();
        IList<LoreChunkRecord> Search(string query, string zoneName = null, string roomName = null, int limit = 5);
    }
}
```

- [ ] Создать `Abstractions/IAiContextBuilder.cs`:

```csharp
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Abstractions
{
    public interface IAiContextBuilder
    {
        string BuildPrompt(string userQuestion, GameSessionState session);
    }
}
```

- [ ] Создать `Abstractions/IAiCommentaryService.cs`:

```csharp
using Adan.Client.Plugins.AI.Events;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Abstractions
{
    public interface IAiCommentaryService
    {
        void OnGameEvent(GameEvent ev, GameSessionState session);
        bool IsEnabled { get; set; }
    }
}
```

- [ ] Создать `Abstractions/IAiCommandHandler.cs`:

```csharp
namespace Adan.Client.Plugins.AI.Abstractions
{
    public interface IAiCommandHandler
    {
        // Returns true if command was handled (starts with /ai)
        bool TryHandle(string commandText);
    }
}
```

- [ ] Собрать, убедиться нет ошибок.

---

### Task A3: Модели данных памяти

**Files:**
- Create: `Adan.Client.Plugins.AI/Memory/RoomRecord.cs`
- Create: `Adan.Client.Plugins.AI/Memory/ExitRecord.cs`
- Create: `Adan.Client.Plugins.AI/Memory/GameEventRecord.cs`
- Create: `Adan.Client.Plugins.AI/Memory/LoreChunkRecord.cs`
- Create: `Adan.Client.Plugins.AI/Memory/GameSessionState.cs`

- [ ] Создать `Memory/RoomRecord.cs`:

```csharp
using System;

namespace Adan.Client.Plugins.AI.Memory
{
    public class RoomRecord
    {
        public long Id { get; set; }
        public long ZoneId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int VisitCount { get; set; }
        public DateTime LastSeenAt { get; set; }
        public string Notes { get; set; }
    }
}
```

- [ ] Создать `Memory/ExitRecord.cs`:

```csharp
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
```

- [ ] Создать `Memory/GameEventRecord.cs`:

```csharp
using System;
using Adan.Client.Plugins.AI.Events;

namespace Adan.Client.Plugins.AI.Memory
{
    public class GameEventRecord
    {
        public long Id { get; set; }
        public DateTime Timestamp { get; set; }
        public GameEventType EventType { get; set; }
        public long? ZoneId { get; set; }
        public long? RoomId { get; set; }
        public string RawText { get; set; }
        public string StructuredDataJson { get; set; }
        public int Importance { get; set; }
    }
}
```

- [ ] Создать `Memory/LoreChunkRecord.cs`:

```csharp
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
```

- [ ] Создать `Memory/GameSessionState.cs`:

```csharp
using System;
using System.Collections.Generic;
using Adan.Client.Plugins.AI.Events;

namespace Adan.Client.Plugins.AI.Memory
{
    public class GameSessionState
    {
        public string CurrentZoneName { get; set; } = string.Empty;
        public long CurrentZoneId { get; set; }
        public string CurrentRoomName { get; set; } = string.Empty;
        public long CurrentRoomId { get; set; }
        public string LastPlayerCommand { get; set; } = string.Empty;
        public List<string> VisibleMobs { get; set; } = new List<string>();
        public List<string> VisibleItems { get; set; } = new List<string>();
        public bool InCombat { get; set; }
        public int? HealthPercent { get; set; }
        public List<GameEvent> RecentImportantEvents { get; set; } = new List<GameEvent>();
        public List<string> RecentLines { get; set; } = new List<string>();
        public DateTime LastCommentaryAt { get; set; } = DateTime.MinValue;
        // For loop detection: last N room names visited
        public List<long> RecentRoomPath { get; set; } = new List<long>();
    }
}
```

- [ ] Собрать, убедиться нет ошибок.

---

### Task A4: GameEventType + GameEvent

**Files:**
- Create: `Adan.Client.Plugins.AI/Events/GameEventType.cs`
- Create: `Adan.Client.Plugins.AI/Events/GameEvent.cs`

- [ ] Создать `Events/GameEventType.cs`:

```csharp
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
```

- [ ] Создать `Events/GameEvent.cs`:

```csharp
using System;

namespace Adan.Client.Plugins.AI.Events
{
    public class GameEvent
    {
        public GameEventType Type { get; set; }
        public string RawText { get; set; }
        public string EntityName { get; set; }  // mob/item/room name
        public string Direction { get; set; }   // for movement events
        public int Importance { get; set; }     // 1=low, 5=high
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
```

- [ ] Собрать.

---

### Task A5: AiSettings

**Files:**
- Create: `Adan.Client.Plugins.AI/Configuration/AiSettings.cs`
- Create: `Adan.Client.Plugins.AI/Configuration/AiSettingsSerializer.cs`

- [ ] Создать `Configuration/AiSettings.cs`:

```csharp
using System;
using System.IO;
using System.Xml.Serialization;

namespace Adan.Client.Plugins.AI.Configuration
{
    [XmlRoot("LocalAi")]
    public class AiSettings
    {
        public bool Enabled { get; set; } = false;
        public string ModelPath { get; set; } = @"Models\qwen3-1.7b-q4_k_m.gguf";
        public int ContextSize { get; set; } = 4096;
        public int MaxResponseTokens { get; set; } = 200;
        public int Threads { get; set; } = Math.Max(2, Environment.ProcessorCount / 2);
        public float Temperature { get; set; } = 0.6f;
        public float TopP { get; set; } = 0.9f;
        public float RepeatPenalty { get; set; } = 1.1f;
        public bool CommentaryEnabled { get; set; } = true;
        public int CommentaryCooldownSeconds { get; set; } = 30;
        public string LoreDirectory { get; set; } = "Lore";
        public string DatabasePath { get; set; } = @"Data\ai-memory.db";
        public string AssistantName { get; set; } = "Лира";
        public int RequestTimeoutSeconds { get; set; } = 90;
        public bool DebugLogPrompts { get; set; } = false;

        [XmlIgnore]
        public string ResolvedModelPath => Path.IsPathRooted(ModelPath)
            ? ModelPath
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ModelPath);

        [XmlIgnore]
        public string ResolvedDatabasePath => Path.IsPathRooted(DatabasePath)
            ? DatabasePath
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DatabasePath);

        [XmlIgnore]
        public string ResolvedLoreDirectory => Path.IsPathRooted(LoreDirectory)
            ? LoreDirectory
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LoreDirectory);
    }
}
```

- [ ] Создать `Configuration/AiSettingsSerializer.cs`:

```csharp
using System;
using System.IO;
using System.Xml.Serialization;

namespace Adan.Client.Plugins.AI.Configuration
{
    public static class AiSettingsSerializer
    {
        private static readonly string SettingsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "ai-settings.xml");
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(AiSettings));

        public static AiSettings Load()
        {
            if (!File.Exists(SettingsPath))
                return new AiSettings();
            try
            {
                using (var reader = File.OpenRead(SettingsPath))
                    return (AiSettings)Serializer.Deserialize(reader);
            }
            catch
            {
                return new AiSettings();
            }
        }

        public static void Save(AiSettings settings)
        {
            using (var writer = File.CreateText(SettingsPath))
                Serializer.Serialize(writer, settings);
        }
    }
}
```

- [ ] Собрать.

---

### Task A6: SQLite схема

**Files:**
- Create: `Adan.Client.Plugins.AI/Memory/DbSchema.cs`

- [ ] Создать `Memory/DbSchema.cs`:

```csharp
using System.Data.SQLite;

namespace Adan.Client.Plugins.AI.Memory
{
    internal static class DbSchema
    {
        private const int CurrentVersion = 1;

        public static void EnsureSchema(SQLiteConnection conn)
        {
            int version = GetVersion(conn);
            if (version < 1) ApplyV1(conn);
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
                cmd.CommandText = $"PRAGMA user_version = {v}";
                cmd.ExecuteNonQuery();
            }
        }

        private static void ApplyV1(SQLiteConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;

CREATE TABLE IF NOT EXISTS Zones (
    Id INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    NormalizedName TEXT NOT NULL,
    FirstSeenAt TEXT NOT NULL,
    LastSeenAt TEXT NOT NULL,
    Summary TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_zones_norm ON Zones(NormalizedName);

CREATE TABLE IF NOT EXISTS Rooms (
    Id INTEGER PRIMARY KEY,
    ZoneId INTEGER NOT NULL REFERENCES Zones(Id),
    Name TEXT NOT NULL,
    NormalizedNameHash TEXT NOT NULL,
    Description TEXT,
    FirstSeenAt TEXT NOT NULL,
    LastSeenAt TEXT NOT NULL,
    VisitCount INTEGER NOT NULL DEFAULT 1,
    Notes TEXT
);
CREATE INDEX IF NOT EXISTS ix_rooms_zone ON Rooms(ZoneId);
CREATE INDEX IF NOT EXISTS ix_rooms_hash ON Rooms(ZoneId, NormalizedNameHash);

CREATE TABLE IF NOT EXISTS RoomExits (
    Id INTEGER PRIMARY KEY,
    FromRoomId INTEGER NOT NULL REFERENCES Rooms(Id),
    Direction TEXT NOT NULL,
    ToRoomId INTEGER REFERENCES Rooms(Id),
    IsConfirmed INTEGER NOT NULL DEFAULT 0,
    FirstSeenAt TEXT NOT NULL,
    LastSeenAt TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_exits_uniq ON RoomExits(FromRoomId, Direction);

CREATE TABLE IF NOT EXISTS Mobs (
    Id INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    NormalizedName TEXT NOT NULL,
    Description TEXT,
    FirstSeenAt TEXT NOT NULL,
    LastSeenAt TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_mobs_norm ON Mobs(NormalizedName);

CREATE TABLE IF NOT EXISTS Items (
    Id INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    NormalizedName TEXT NOT NULL,
    Description TEXT,
    FirstSeenAt TEXT NOT NULL,
    LastSeenAt TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_items_norm ON Items(NormalizedName);

CREATE TABLE IF NOT EXISTS RoomMobs (
    RoomId INTEGER NOT NULL REFERENCES Rooms(Id),
    MobId INTEGER NOT NULL REFERENCES Mobs(Id),
    LastSeenAt TEXT NOT NULL,
    PRIMARY KEY(RoomId, MobId)
);

CREATE TABLE IF NOT EXISTS RoomItems (
    RoomId INTEGER NOT NULL REFERENCES Rooms(Id),
    ItemId INTEGER NOT NULL REFERENCES Items(Id),
    LastSeenAt TEXT NOT NULL,
    PRIMARY KEY(RoomId, ItemId)
);

CREATE TABLE IF NOT EXISTS GameEvents (
    Id INTEGER PRIMARY KEY,
    Timestamp TEXT NOT NULL,
    EventType INTEGER NOT NULL,
    ZoneId INTEGER REFERENCES Zones(Id),
    RoomId INTEGER REFERENCES Rooms(Id),
    RawText TEXT,
    StructuredDataJson TEXT,
    Importance INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_events_zone ON GameEvents(ZoneId, Timestamp);

CREATE TABLE IF NOT EXISTS UserNotes (
    Id INTEGER PRIMARY KEY,
    CreatedAt TEXT NOT NULL,
    Text TEXT NOT NULL,
    ZoneId INTEGER REFERENCES Zones(Id),
    RoomId INTEGER REFERENCES Rooms(Id)
);

CREATE TABLE IF NOT EXISTS LoreDocuments (
    Path TEXT PRIMARY KEY,
    Title TEXT,
    ContentHash INTEGER NOT NULL,
    IndexedAt TEXT NOT NULL
);

CREATE VIRTUAL TABLE IF NOT EXISTS LoreChunksFts USING fts5(
    DocPath UNINDEXED,
    DocTitle,
    SectionTitle,
    Content,
    content='',
    tokenize='unicode61'
);

CREATE TABLE IF NOT EXISTS LoreChunks (
    Id INTEGER PRIMARY KEY,
    DocPath TEXT NOT NULL,
    ChunkIndex INTEGER NOT NULL,
    SectionTitle TEXT,
    Content TEXT NOT NULL,
    FtsRowId INTEGER
);
CREATE INDEX IF NOT EXISTS ix_chunks_doc ON LoreChunks(DocPath);
";
                cmd.ExecuteNonQuery();
            }
        }
    }
}
```

- [ ] Собрать.

---

### Task A7: GameMemoryService — базовая реализация

**Files:**
- Create: `Adan.Client.Plugins.AI/Memory/GameMemoryService.cs`

- [ ] Создать `Memory/GameMemoryService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using Adan.Client.Plugins.AI.Abstractions;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Events;

namespace Adan.Client.Plugins.AI.Memory
{
    public class GameMemoryService : IGameMemoryService
    {
        private readonly AiSettings _settings;
        private SQLiteConnection _conn;

        public GameMemoryService(AiSettings settings) { _settings = settings; }

        public void Initialize()
        {
            string dbPath = _settings.ResolvedDatabasePath;
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath));
            _conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
            _conn.Open();
            DbSchema.EnsureSchema(_conn);
        }

        private static string Now() => DateTime.UtcNow.ToString("O");

        private static string Normalize(string s) =>
            (s ?? string.Empty).Trim().ToLowerInvariant();

        public long UpsertZone(string name)
        {
            string norm = Normalize(name);
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO Zones(Name, NormalizedName, FirstSeenAt, LastSeenAt)
                    VALUES(@n, @norm, @now, @now)
                    ON CONFLICT(NormalizedName) DO UPDATE SET LastSeenAt=@now;
                    SELECT Id FROM Zones WHERE NormalizedName=@norm;";
                cmd.Parameters.AddWithValue("@n", name);
                cmd.Parameters.AddWithValue("@norm", norm);
                cmd.Parameters.AddWithValue("@now", Now());
                return (long)cmd.ExecuteScalar();
            }
        }

        public long UpsertRoom(long zoneId, string name, string description)
        {
            string hash = Normalize(name);
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO Rooms(ZoneId, Name, NormalizedNameHash, Description, FirstSeenAt, LastSeenAt, VisitCount)
                    VALUES(@z, @n, @h, @d, @now, @now, 1)
                    ON CONFLICT DO NOTHING;
                    UPDATE Rooms SET LastSeenAt=@now, VisitCount=VisitCount+1 WHERE ZoneId=@z AND NormalizedNameHash=@h;
                    SELECT Id FROM Rooms WHERE ZoneId=@z AND NormalizedNameHash=@h;";
                cmd.Parameters.AddWithValue("@z", zoneId);
                cmd.Parameters.AddWithValue("@n", name);
                cmd.Parameters.AddWithValue("@h", hash);
                cmd.Parameters.AddWithValue("@d", (object)description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@now", Now());
                return (long)cmd.ExecuteScalar();
            }
        }

        public void ConfirmExit(long fromRoomId, string direction, long toRoomId)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO RoomExits(FromRoomId, Direction, ToRoomId, IsConfirmed, FirstSeenAt, LastSeenAt)
                    VALUES(@f, @dir, @t, 1, @now, @now)
                    ON CONFLICT(FromRoomId, Direction) DO UPDATE SET ToRoomId=@t, IsConfirmed=1, LastSeenAt=@now;";
                cmd.Parameters.AddWithValue("@f", fromRoomId);
                cmd.Parameters.AddWithValue("@dir", direction.ToLowerInvariant());
                cmd.Parameters.AddWithValue("@t", toRoomId);
                cmd.Parameters.AddWithValue("@now", Now());
                cmd.ExecuteNonQuery();
            }
        }

        public IList<RoomRecord> GetRoomsInZone(long zoneId)
        {
            var list = new List<RoomRecord>();
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, Name, Description, VisitCount, LastSeenAt, Notes FROM Rooms WHERE ZoneId=@z";
                cmd.Parameters.AddWithValue("@z", zoneId);
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new RoomRecord
                        {
                            Id = r.GetInt64(0), ZoneId = zoneId,
                            Name = r.GetString(1),
                            Description = r.IsDBNull(2) ? null : r.GetString(2),
                            VisitCount = r.GetInt32(3),
                            LastSeenAt = DateTime.Parse(r.GetString(4)),
                            Notes = r.IsDBNull(5) ? null : r.GetString(5)
                        });
            }
            return list;
        }

        public IList<ExitRecord> GetExitsFromRoom(long roomId)
        {
            var list = new List<ExitRecord>();
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, Direction, ToRoomId, IsConfirmed FROM RoomExits WHERE FromRoomId=@r";
                cmd.Parameters.AddWithValue("@r", roomId);
                using (var reader = cmd.ExecuteReader())
                    while (reader.Read())
                        list.Add(new ExitRecord
                        {
                            Id = reader.GetInt64(0),
                            FromRoomId = roomId,
                            Direction = reader.GetString(1),
                            ToRoomId = reader.IsDBNull(2) ? (long?)null : reader.GetInt64(2),
                            IsConfirmed = reader.GetBoolean(3)
                        });
            }
            return list;
        }

        public IList<long> FindShortestPath(long fromRoomId, long toRoomId)
        {
            // BFS по графу переходов
            var visited = new HashSet<long>();
            var queue = new Queue<List<long>>();
            queue.Enqueue(new List<long> { fromRoomId });
            visited.Add(fromRoomId);

            while (queue.Count > 0)
            {
                var path = queue.Dequeue();
                long current = path[path.Count - 1];
                if (current == toRoomId) return path;
                foreach (var exit in GetExitsFromRoom(current))
                {
                    if (exit.ToRoomId.HasValue && !visited.Contains(exit.ToRoomId.Value))
                    {
                        visited.Add(exit.ToRoomId.Value);
                        var newPath = new List<long>(path) { exit.ToRoomId.Value };
                        queue.Enqueue(newPath);
                    }
                }
                if (visited.Count > 5000) break; // защита от бесконечного обхода
            }
            return new List<long>();
        }

        public long UpsertMob(string name, string description = null)
        {
            string norm = Normalize(name);
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO Mobs(Name, NormalizedName, Description, FirstSeenAt, LastSeenAt)
                    VALUES(@n, @norm, @d, @now, @now)
                    ON CONFLICT(NormalizedName) DO UPDATE SET LastSeenAt=@now;
                    SELECT Id FROM Mobs WHERE NormalizedName=@norm;";
                cmd.Parameters.AddWithValue("@n", name);
                cmd.Parameters.AddWithValue("@norm", norm);
                cmd.Parameters.AddWithValue("@d", (object)description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@now", Now());
                return (long)cmd.ExecuteScalar();
            }
        }

        public long UpsertItem(string name, string description = null)
        {
            string norm = Normalize(name);
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO Items(Name, NormalizedName, Description, FirstSeenAt, LastSeenAt)
                    VALUES(@n, @norm, @d, @now, @now)
                    ON CONFLICT(NormalizedName) DO UPDATE SET LastSeenAt=@now;
                    SELECT Id FROM Items WHERE NormalizedName=@norm;";
                cmd.Parameters.AddWithValue("@n", name);
                cmd.Parameters.AddWithValue("@norm", norm);
                cmd.Parameters.AddWithValue("@d", (object)description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@now", Now());
                return (long)cmd.ExecuteScalar();
            }
        }

        public void RecordMobInRoom(long roomId, long mobId)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"INSERT OR REPLACE INTO RoomMobs(RoomId, MobId, LastSeenAt) VALUES(@r,@m,@now)";
                cmd.Parameters.AddWithValue("@r", roomId);
                cmd.Parameters.AddWithValue("@m", mobId);
                cmd.Parameters.AddWithValue("@now", Now());
                cmd.ExecuteNonQuery();
            }
        }

        public void RecordItemInRoom(long roomId, long itemId)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"INSERT OR REPLACE INTO RoomItems(RoomId, ItemId, LastSeenAt) VALUES(@r,@i,@now)";
                cmd.Parameters.AddWithValue("@r", roomId);
                cmd.Parameters.AddWithValue("@i", itemId);
                cmd.Parameters.AddWithValue("@now", Now());
                cmd.ExecuteNonQuery();
            }
        }

        public void SaveEvent(GameEventRecord ev)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO GameEvents(Timestamp,EventType,ZoneId,RoomId,RawText,StructuredDataJson,Importance)
                    VALUES(@ts,@et,@z,@r,@raw,@json,@imp)";
                cmd.Parameters.AddWithValue("@ts", ev.Timestamp.ToString("O"));
                cmd.Parameters.AddWithValue("@et", (int)ev.EventType);
                cmd.Parameters.AddWithValue("@z", ev.ZoneId.HasValue ? (object)ev.ZoneId.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@r", ev.RoomId.HasValue ? (object)ev.RoomId.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@raw", (object)ev.RawText ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@json", (object)ev.StructuredDataJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@imp", ev.Importance);
                cmd.ExecuteNonQuery();
            }
        }

        public IList<GameEventRecord> GetRecentEvents(long? zoneId, int limit = 20)
        {
            var list = new List<GameEventRecord>();
            using (var cmd = _conn.CreateCommand())
            {
                if (zoneId.HasValue)
                {
                    cmd.CommandText = "SELECT Id,Timestamp,EventType,ZoneId,RoomId,RawText,Importance FROM GameEvents WHERE ZoneId=@z ORDER BY Timestamp DESC LIMIT @lim";
                    cmd.Parameters.AddWithValue("@z", zoneId.Value);
                }
                else
                {
                    cmd.CommandText = "SELECT Id,Timestamp,EventType,ZoneId,RoomId,RawText,Importance FROM GameEvents ORDER BY Timestamp DESC LIMIT @lim";
                }
                cmd.Parameters.AddWithValue("@lim", limit);
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new GameEventRecord
                        {
                            Id = r.GetInt64(0),
                            Timestamp = DateTime.Parse(r.GetString(1)),
                            EventType = (GameEventType)r.GetInt32(2),
                            ZoneId = r.IsDBNull(3) ? (long?)null : r.GetInt64(3),
                            RoomId = r.IsDBNull(4) ? (long?)null : r.GetInt64(4),
                            RawText = r.IsDBNull(5) ? null : r.GetString(5),
                            Importance = r.GetInt32(6)
                        });
            }
            return list;
        }

        public void SaveUserNote(string text, long? zoneId, long? roomId)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO UserNotes(CreatedAt,Text,ZoneId,RoomId) VALUES(@t,@txt,@z,@r)";
                cmd.Parameters.AddWithValue("@t", Now());
                cmd.Parameters.AddWithValue("@txt", text);
                cmd.Parameters.AddWithValue("@z", zoneId.HasValue ? (object)zoneId.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@r", roomId.HasValue ? (object)roomId.Value : DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        public string GetZoneSummary(long zoneId)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Summary FROM Zones WHERE Id=@z";
                cmd.Parameters.AddWithValue("@z", zoneId);
                var result = cmd.ExecuteScalar();
                return result as string;
            }
        }

        public void SaveZoneSummary(long zoneId, string summary)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE Zones SET Summary=@s WHERE Id=@z";
                cmd.Parameters.AddWithValue("@s", summary);
                cmd.Parameters.AddWithValue("@z", zoneId);
                cmd.ExecuteNonQuery();
            }
        }

        public void SaveLoreDocument(string path, string title, long contentHash)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO LoreDocuments(Path,Title,ContentHash,IndexedAt) VALUES(@p,@t,@h,@now)
                    ON CONFLICT(Path) DO UPDATE SET ContentHash=@h, IndexedAt=@now, Title=@t";
                cmd.Parameters.AddWithValue("@p", path);
                cmd.Parameters.AddWithValue("@t", (object)title ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@h", contentHash);
                cmd.Parameters.AddWithValue("@now", Now());
                cmd.ExecuteNonQuery();
            }
        }

        public bool LoreDocumentChanged(string path, long contentHash)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "SELECT ContentHash FROM LoreDocuments WHERE Path=@p";
                cmd.Parameters.AddWithValue("@p", path);
                var stored = cmd.ExecuteScalar();
                return stored == null || (long)stored != contentHash;
            }
        }

        public void SaveLoreChunk(string docPath, int chunkIndex, string content, string sectionTitle)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO LoreChunks(DocPath, ChunkIndex, SectionTitle, Content) VALUES(@p,@i,@s,@c)";
                cmd.Parameters.AddWithValue("@p", docPath);
                cmd.Parameters.AddWithValue("@i", chunkIndex);
                cmd.Parameters.AddWithValue("@s", (object)sectionTitle ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@c", content);
                long rowId = _conn.LastInsertRowId;
                // Insert into FTS
                cmd.CommandText = "INSERT INTO LoreChunksFts(rowid, DocTitle, SectionTitle, Content) VALUES(@rowid, @dt, @s2, @c2)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@rowid", rowId);
                cmd.Parameters.AddWithValue("@dt", Path.GetFileNameWithoutExtension(docPath));
                cmd.Parameters.AddWithValue("@s2", (object)sectionTitle ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@c2", content);
                cmd.ExecuteNonQuery();
            }
        }

        public IList<LoreChunkRecord> SearchLore(string query, int limit = 5)
        {
            var list = new List<LoreChunkRecord>();
            if (string.IsNullOrWhiteSpace(query)) return list;
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT lc.DocPath, lc.SectionTitle, lc.Content, bm25(LoreChunksFts) as score
                    FROM LoreChunksFts
                    JOIN LoreChunks lc ON lc.rowid = LoreChunksFts.rowid
                    WHERE LoreChunksFts MATCH @q
                    ORDER BY score
                    LIMIT @lim";
                cmd.Parameters.AddWithValue("@q", query.Replace("\"", "") + "*");
                cmd.Parameters.AddWithValue("@lim", limit);
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new LoreChunkRecord
                        {
                            DocPath = r.GetString(0),
                            DocTitle = Path.GetFileNameWithoutExtension(r.GetString(0)),
                            SectionTitle = r.IsDBNull(1) ? null : r.GetString(1),
                            Content = r.GetString(2),
                            Score = r.GetDouble(3)
                        });
            }
            return list;
        }

        public void Dispose() { _conn?.Close(); _conn?.Dispose(); }
    }
}
```

- [ ] Собрать.

---

### Task A8: AiPlugin — MEF-точка входа

**Files:**
- Create: `Adan.Client.Plugins.AI/AiPlugin.cs`

- [ ] Создать `AiPlugin.cs`:

```csharp
using System.ComponentModel.Composition;
using System.Windows;
using Adan.Client.Common.Conveyor;
using Adan.Client.Common.Model;
using Adan.Client.Common.Plugins;
using Adan.Client.Common.ViewModel;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI
{
    [Export(typeof(PluginBase))]
    public sealed class AiPlugin : PluginBase
    {
        private AiSettings _settings;
        private GameMemoryService _memory;

        public override string Name => "LocalAI";

        public override void InitializeConveyor(MessageConveyor conveyor)
        {
            _settings = AiSettingsSerializer.Load();
            if (!_settings.Enabled) return;

            _memory = new GameMemoryService(_settings);
            try { _memory.Initialize(); }
            catch { /* DB недоступна — работаем без памяти */ }

            // ConveyorUnit будет добавлен в следующем этапе
        }

        public override void Initialize(InitializationStatusModel initializationStatusModel, Window mainWindow)
        {
            initializationStatusModel.CurrentPluginName = "Local AI";
            initializationStatusModel.PluginInitializationStatus = "Initializing";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _memory?.Dispose();
            base.Dispose(disposing);
        }
    }
}
```

- [ ] Добавить `Adan.Client.Plugins.AI` в solution (`Adan.Client2017.sln`).
- [ ] Собрать весь solution.
- [ ] Убедиться что DLL появляется в `bin/Debug/`.

---

### Task A9: Тест-проект для AI

**Files:**
- Create: `Adan.Client.Plugins.AI.Tests/Adan.Client.Plugins.AI.Tests.csproj`
- Create: `Adan.Client.Plugins.AI.Tests/Memory/GameMemoryServiceTests.cs`

- [ ] Создать `Adan.Client.Plugins.AI.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NUnit" Version="3.14.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.5.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="System.Data.SQLite" Version="1.0.118.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Adan.Client.Plugins.AI\Adan.Client.Plugins.AI.csproj" />
    <ProjectReference Include="..\Adan.Client.Common\Adan.Client.Common.csproj" />
  </ItemGroup>
</Project>
```

- [ ] Создать `Memory/GameMemoryServiceTests.cs`:

```csharp
using System.IO;
using NUnit.Framework;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Events;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Tests.Memory
{
    [TestFixture]
    public class GameMemoryServiceTests
    {
        private string _dbPath;
        private GameMemoryService _svc;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.GetTempFileName() + ".db";
            var settings = new AiSettings { DatabasePath = _dbPath };
            _svc = new GameMemoryService(settings);
            _svc.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            _svc.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Test]
        public void UpsertZone_ReturnsSameIdForSameName()
        {
            long id1 = _svc.UpsertZone("Тёмный лес");
            long id2 = _svc.UpsertZone("Тёмный лес");
            Assert.That(id1, Is.EqualTo(id2));
        }

        [Test]
        public void UpsertZone_NormalizesCase()
        {
            long id1 = _svc.UpsertZone("ТЁМНЫЙ ЛЕС");
            long id2 = _svc.UpsertZone("тёмный лес");
            Assert.That(id1, Is.EqualTo(id2));
        }

        [Test]
        public void UpsertRoom_IncreasesVisitCount()
        {
            long zoneId = _svc.UpsertZone("Лес");
            _svc.UpsertRoom(zoneId, "Развилка", null);
            _svc.UpsertRoom(zoneId, "Развилка", null);
            var rooms = _svc.GetRoomsInZone(zoneId);
            Assert.That(rooms[0].VisitCount, Is.EqualTo(2));
        }

        [Test]
        public void ConfirmExit_StoredAndRetrieved()
        {
            long zoneId = _svc.UpsertZone("Лес");
            long r1 = _svc.UpsertRoom(zoneId, "Старт", null);
            long r2 = _svc.UpsertRoom(zoneId, "Финиш", null);
            _svc.ConfirmExit(r1, "n", r2);
            var exits = _svc.GetExitsFromRoom(r1);
            Assert.That(exits, Has.Count.EqualTo(1));
            Assert.That(exits[0].Direction, Is.EqualTo("n"));
            Assert.That(exits[0].ToRoomId, Is.EqualTo(r2));
            Assert.That(exits[0].IsConfirmed, Is.True);
        }

        [Test]
        public void FindShortestPath_TwoHops()
        {
            long zoneId = _svc.UpsertZone("Лес");
            long r1 = _svc.UpsertRoom(zoneId, "A", null);
            long r2 = _svc.UpsertRoom(zoneId, "B", null);
            long r3 = _svc.UpsertRoom(zoneId, "C", null);
            _svc.ConfirmExit(r1, "n", r2);
            _svc.ConfirmExit(r2, "n", r3);
            var path = _svc.FindShortestPath(r1, r3);
            Assert.That(path, Is.EqualTo(new[] { r1, r2, r3 }));
        }

        [Test]
        public void FindShortestPath_NoPath_ReturnsEmpty()
        {
            long zoneId = _svc.UpsertZone("Лес");
            long r1 = _svc.UpsertRoom(zoneId, "A", null);
            long r2 = _svc.UpsertRoom(zoneId, "B", null);
            var path = _svc.FindShortestPath(r1, r2);
            Assert.That(path, Is.Empty);
        }

        [Test]
        public void SaveAndGetRecentEvents()
        {
            long zoneId = _svc.UpsertZone("Лес");
            _svc.SaveEvent(new GameEventRecord
            {
                Timestamp = System.DateTime.UtcNow,
                EventType = GameEventType.MobKilled,
                ZoneId = zoneId,
                RawText = "Тролль убит!",
                Importance = 3
            });
            var events = _svc.GetRecentEvents(zoneId, 10);
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].RawText, Is.EqualTo("Тролль убит!"));
        }

        [Test]
        public void LoreDocumentChanged_TrueForNewDoc()
        {
            Assert.That(_svc.LoreDocumentChanged("test.md", 12345), Is.True);
        }

        [Test]
        public void LoreDocumentChanged_FalseAfterSave()
        {
            _svc.SaveLoreDocument("test.md", "Test", 12345);
            Assert.That(_svc.LoreDocumentChanged("test.md", 12345), Is.False);
        }

        [Test]
        public void LoreDocumentChanged_TrueAfterContentChange()
        {
            _svc.SaveLoreDocument("test.md", "Test", 12345);
            Assert.That(_svc.LoreDocumentChanged("test.md", 99999), Is.True);
        }
    }
}
```

- [ ] Запустить тесты: `dotnet test Adan.Client.Plugins.AI.Tests\Adan.Client.Plugins.AI.Tests.csproj`
- [ ] Убедиться что все 9 тестов проходят.
- [ ] Commit:

```
git add Adan.Client.Plugins.AI\ Adan.Client.Plugins.AI.Tests\
git commit -m "feat(ai): Этап A — скелет плагина, интерфейсы, SQLite, тесты"
```

---

## ЭТАП B: Сайдкар-процесс + инференс + команда /ai

### Task B1: Создать Adan.AI.Host (.NET 8 сайдкар)

**Files:**
- Create: `Adan.AI.Host/Adan.AI.Host.csproj`
- Create: `Adan.AI.Host/PipeProtocol.cs`
- Create: `Adan.AI.Host/HostSettings.cs`
- Create: `Adan.AI.Host/LlmEngine.cs`
- Create: `Adan.AI.Host/Program.cs`

- [ ] Создать `Adan.AI.Host/Adan.AI.Host.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <AssemblyName>Adan.AI.Host</AssemblyName>
    <RollForward>LatestMajor</RollForward>
    <SelfContained>false</SelfContained>
    <PlatformTarget>x86</PlatformTarget>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="LLamaSharp" Version="0.20.0" />
    <PackageReference Include="LLamaSharp.Backend.Cpu" Version="0.20.0" />
  </ItemGroup>
</Project>
```

- [ ] Создать `Adan.AI.Host/PipeProtocol.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Adan.AI.Host
{
    public class LlmRequest
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("prompt")] public string Prompt { get; set; } = "";
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; } = 200;
        [JsonPropertyName("cancel")] public bool Cancel { get; set; } = false;
    }

    public class LlmResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("done")] public bool Done { get; set; } = false;
        [JsonPropertyName("token")] public string? Token { get; set; } // streaming
    }

    public class HostCommand
    {
        [JsonPropertyName("cmd")] public string Cmd { get; set; } = ""; // "load","unload","status"
        [JsonPropertyName("model_path")] public string? ModelPath { get; set; }
        [JsonPropertyName("context_size")] public int ContextSize { get; set; } = 4096;
        [JsonPropertyName("threads")] public int Threads { get; set; } = 4;
        [JsonPropertyName("temperature")] public float Temperature { get; set; } = 0.6f;
        [JsonPropertyName("top_p")] public float TopP { get; set; } = 0.9f;
        [JsonPropertyName("repeat_penalty")] public float RepeatPenalty { get; set; } = 1.1f;
    }

    public class HostResponse
    {
        [JsonPropertyName("cmd")] public string Cmd { get; set; } = "";
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("error")] public string? Error { get; set; }
    }
}
```

- [ ] Создать `Adan.AI.Host/LlmEngine.cs`:

```csharp
using LLama;
using LLama.Common;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Adan.AI.Host
{
    public class LlmEngine : IDisposable
    {
        private LLamaWeights? _weights;
        private LLamaContext? _ctx;
        private ModelParams? _params;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private CancellationTokenSource? _currentCts;

        public bool IsLoaded => _weights != null;

        public void Load(string modelPath, int contextSize, int threads, float temperature, float topP, float repeatPenalty)
        {
            Unload();
            _params = new ModelParams(modelPath)
            {
                ContextSize = (uint)contextSize,
                Threads = threads,
                GpuLayerCount = 0,
            };
            _weights = LLamaWeights.LoadFromFile(_params);
            _ctx = _weights.CreateContext(_params);
        }

        public void Unload()
        {
            _currentCts?.Cancel();
            _ctx?.Dispose(); _ctx = null;
            _weights?.Dispose(); _weights = null;
        }

        public async Task<string> GenerateAsync(string prompt, int maxTokens, CancellationToken externalCt)
        {
            if (_weights == null || _ctx == null) throw new InvalidOperationException("Model not loaded");
            await _lock.WaitAsync(externalCt);
            _currentCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            try
            {
                var ex = new InteractiveExecutor(_ctx);
                var inferParams = new InferenceParams
                {
                    MaxTokens = maxTokens,
                    AntiPrompts = new[] { "User:", "\nUser:" }
                };
                var sb = new System.Text.StringBuilder();
                await foreach (var token in ex.InferAsync(prompt, inferParams, _currentCts.Token))
                    sb.Append(token);
                return sb.ToString().Trim();
            }
            finally
            {
                _currentCts = null;
                _lock.Release();
            }
        }

        public void CancelCurrent() => _currentCts?.Cancel();

        public void Dispose() { Unload(); _lock.Dispose(); }
    }
}
```

- [ ] Создать `Adan.AI.Host/Program.cs`:

```csharp
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Adan.AI.Host
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string pipeName = args.Length > 0 ? args[0] : "AdanAiPipe";
            using var engine = new LlmEngine();
            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
                        1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(cts.Token);
                    await HandleConnectionAsync(server, engine, cts.Token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Console.Error.WriteLine($"Pipe error: {ex.Message}"); }
            }
        }

        static async Task HandleConnectionAsync(NamedPipeServerStream pipe, LlmEngine engine, CancellationToken ct)
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            while (!ct.IsCancellationRequested && pipe.IsConnected)
            {
                string? line;
                try { line = await reader.ReadLineAsync(); }
                catch { break; }
                if (line == null) break;

                // Detect message type by first field
                if (line.Contains("\"cmd\""))
                {
                    var cmd = JsonSerializer.Deserialize<HostCommand>(line);
                    var resp = new HostResponse { Cmd = cmd!.Cmd };
                    try
                    {
                        if (cmd.Cmd == "load")
                        {
                            engine.Load(cmd.ModelPath!, cmd.ContextSize, cmd.Threads, cmd.Temperature, cmd.TopP, cmd.RepeatPenalty);
                            resp.Status = "loaded";
                        }
                        else if (cmd.Cmd == "unload")
                        {
                            engine.Unload();
                            resp.Status = "unloaded";
                        }
                        else if (cmd.Cmd == "status")
                        {
                            resp.Status = engine.IsLoaded ? "ready" : "idle";
                        }
                    }
                    catch (Exception ex) { resp.Status = "error"; resp.Error = ex.Message; }
                    await writer.WriteLineAsync(JsonSerializer.Serialize(resp));
                }
                else
                {
                    var req = JsonSerializer.Deserialize<LlmRequest>(line);
                    if (req!.Cancel) { engine.CancelCurrent(); continue; }
                    try
                    {
                        using var reqCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, reqCts.Token);
                        string text = await engine.GenerateAsync(req.Prompt, req.MaxTokens, linked.Token);
                        var resp = new LlmResponse { Id = req.Id, Text = text, Done = true };
                        await writer.WriteLineAsync(JsonSerializer.Serialize(resp));
                    }
                    catch (Exception ex)
                    {
                        var resp = new LlmResponse { Id = req.Id, Error = ex.Message, Done = true };
                        await writer.WriteLineAsync(JsonSerializer.Serialize(resp));
                    }
                }
            }
        }
    }
}
```

- [ ] Собрать: `dotnet build Adan.AI.Host\Adan.AI.Host.csproj`
- [ ] Убедиться что сборка проходит (без модели — просто компиляция).

---

### Task B2: LocalLlmService — управление сайдкаром из плагина

**Files:**
- Create: `Adan.Client.Plugins.AI/Inference/PipeProtocol.cs`
- Create: `Adan.Client.Plugins.AI/Inference/LocalLlmService.cs`

- [ ] Создать `Inference/PipeProtocol.cs` (зеркало серверного, без зависимостей LLamaSharp):

```csharp
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Adan.Client.Plugins.AI.Inference
{
    [DataContract]
    public class LlmRequest
    {
        [DataMember(Name = "id")] public string Id { get; set; } = "";
        [DataMember(Name = "prompt")] public string Prompt { get; set; } = "";
        [DataMember(Name = "max_tokens")] public int MaxTokens { get; set; } = 200;
        [DataMember(Name = "cancel")] public bool Cancel { get; set; } = false;
    }

    [DataContract]
    public class LlmResponse
    {
        [DataMember(Name = "id")] public string Id { get; set; } = "";
        [DataMember(Name = "text")] public string Text { get; set; } = "";
        [DataMember(Name = "error")] public string Error { get; set; }
        [DataMember(Name = "done")] public bool Done { get; set; } = false;
    }

    [DataContract]
    public class HostCommand
    {
        [DataMember(Name = "cmd")] public string Cmd { get; set; } = "";
        [DataMember(Name = "model_path")] public string ModelPath { get; set; }
        [DataMember(Name = "context_size")] public int ContextSize { get; set; } = 4096;
        [DataMember(Name = "threads")] public int Threads { get; set; } = 4;
        [DataMember(Name = "temperature")] public float Temperature { get; set; } = 0.6f;
        [DataMember(Name = "top_p")] public float TopP { get; set; } = 0.9f;
        [DataMember(Name = "repeat_penalty")] public float RepeatPenalty { get; set; } = 1.1f;
    }

    [DataContract]
    public class HostResponse
    {
        [DataMember(Name = "cmd")] public string Cmd { get; set; } = "";
        [DataMember(Name = "status")] public string Status { get; set; } = "";
        [DataMember(Name = "error")] public string Error { get; set; }
    }
}
```

- [ ] Добавить в `.csproj` плагина `<Reference Include="System.Runtime.Serialization" />`.

- [ ] Создать `Inference/LocalLlmService.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Adan.Client.Plugins.AI.Abstractions;
using Adan.Client.Plugins.AI.Configuration;

namespace Adan.Client.Plugins.AI.Inference
{
    public class LocalLlmService : ILocalLlmService
    {
        private readonly AiSettings _settings;
        private Process _hostProcess;
        private NamedPipeClientStream _pipe;
        private StreamReader _reader;
        private StreamWriter _writer;
        private readonly object _pipeLock = new object();
        private LlmStatus _status = LlmStatus.Disabled;
        private string _reqId;
        private readonly DataContractJsonSerializer _reqSerializer = new DataContractJsonSerializer(typeof(LlmRequest));
        private readonly DataContractJsonSerializer _respSerializer = new DataContractJsonSerializer(typeof(LlmResponse));
        private readonly DataContractJsonSerializer _cmdSerializer = new DataContractJsonSerializer(typeof(HostCommand));
        private readonly DataContractJsonSerializer _hostRespSerializer = new DataContractJsonSerializer(typeof(HostResponse));

        private static string PipeName => $"AdanAiPipe_{Process.GetCurrentProcess().Id}";
        private static string HostExePath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "AI", "Adan.AI.Host.exe");

        public LlmStatus Status { get => _status; private set { _status = value; StatusChanged?.Invoke(this, value); } }
        public event EventHandler<LlmStatus> StatusChanged;

        public LocalLlmService(AiSettings settings) { _settings = settings; }

        public async Task LoadModelAsync(CancellationToken ct = default)
        {
            if (!File.Exists(_settings.ResolvedModelPath))
            {
                Status = LlmStatus.ModelNotFound;
                return;
            }
            Status = LlmStatus.Loading;
            try
            {
                await EnsureHostRunningAsync(ct);
                var cmd = new HostCommand
                {
                    Cmd = "load",
                    ModelPath = _settings.ResolvedModelPath,
                    ContextSize = _settings.ContextSize,
                    Threads = _settings.Threads,
                    Temperature = _settings.Temperature,
                    TopP = _settings.TopP,
                    RepeatPenalty = _settings.RepeatPenalty
                };
                var resp = await SendCommandAsync(cmd, ct);
                Status = resp?.Status == "loaded" ? LlmStatus.Ready : LlmStatus.Error;
            }
            catch (Exception)
            {
                Status = LlmStatus.Error;
            }
        }

        public void UnloadModel()
        {
            try { SendCommandAsync(new HostCommand { Cmd = "unload" }, CancellationToken.None).Wait(3000); }
            catch { }
            Status = LlmStatus.Disabled;
        }

        public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
        {
            if (Status != LlmStatus.Ready) throw new InvalidOperationException("Model not ready");
            Status = LlmStatus.Generating;
            try
            {
                _reqId = Guid.NewGuid().ToString("N");
                var req = new LlmRequest { Id = _reqId, Prompt = prompt, MaxTokens = _settings.MaxResponseTokens };
                string json = Serialize(_reqSerializer, req);
                lock (_pipeLock) _writer.WriteLine(json);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct,
                    new CancellationTokenSource(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds)).Token);
                string line = await ReadLineAsync(timeout.Token);
                var resp = Deserialize<LlmResponse>(_respSerializer, line);
                if (resp.Error != null) throw new Exception(resp.Error);
                return resp.Text ?? string.Empty;
            }
            finally { Status = LlmStatus.Ready; }
        }

        public void CancelCurrent()
        {
            try
            {
                var req = new LlmRequest { Cancel = true };
                lock (_pipeLock) _writer.WriteLine(Serialize(_reqSerializer, req));
            }
            catch { }
        }

        private async Task EnsureHostRunningAsync(CancellationToken ct)
        {
            if (_hostProcess != null && !_hostProcess.HasExited) return;
            _hostProcess = new Process
            {
                StartInfo = new ProcessStartInfo(HostExePath, PipeName)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                }
            };
            _hostProcess.Start();
            _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            for (int i = 0; i < 20; i++)
            {
                try { _pipe.Connect(500); break; }
                catch { await Task.Delay(500, ct); }
            }
            _reader = new StreamReader(_pipe, Encoding.UTF8);
            _writer = new StreamWriter(_pipe, Encoding.UTF8) { AutoFlush = true };
        }

        private async Task<HostResponse> SendCommandAsync(HostCommand cmd, CancellationToken ct)
        {
            string json = Serialize(_cmdSerializer, cmd);
            lock (_pipeLock) _writer.WriteLine(json);
            string line = await ReadLineAsync(ct);
            return Deserialize<HostResponse>(_hostRespSerializer, line);
        }

        private async Task<string> ReadLineAsync(CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>();
            ct.Register(() => tcs.TrySetCanceled());
            var readTask = _reader.ReadLineAsync();
            var completed = await Task.WhenAny(readTask, tcs.Task);
            if (completed == tcs.Task) throw new OperationCanceledException(ct);
            return await readTask;
        }

        private static string Serialize<T>(DataContractJsonSerializer s, T obj)
        {
            using var ms = new MemoryStream();
            s.WriteObject(ms, obj);
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static T Deserialize<T>(DataContractJsonSerializer s, string json)
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "{}"));
            return (T)s.ReadObject(ms);
        }

        public void Dispose()
        {
            try { _writer?.Close(); _reader?.Close(); _pipe?.Close(); }
            catch { }
            try { if (_hostProcess != null && !_hostProcess.HasExited) _hostProcess.Kill(); }
            catch { }
        }
    }
}
```

- [ ] Собрать.

---

### Task B3: AiConveyorUnit — перехват команд и текста

**Files:**
- Create: `Adan.Client.Plugins.AI/Commands/AiConveyorUnit.cs`
- Create: `Adan.Client.Plugins.AI/Commands/AiCommandHandler.cs`

- [ ] Создать `Commands/AiConveyorUnit.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Adan.Client.Common.Commands;
using Adan.Client.Common.Conveyor;
using Adan.Client.Common.ConveyorUnits;
using Adan.Client.Common.Messages;
using Adan.Client.Common.Themes;
using Adan.Client.Plugins.AI.Abstractions;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Commands
{
    public class AiConveyorUnit : ConveyorUnit
    {
        private readonly IAiCommandHandler _commandHandler;
        private readonly IAiCommentaryService _commentary;
        private readonly GameEventExtractor _extractor;
        private readonly GameSessionState _session;

        public AiConveyorUnit(
            MessageConveyor conveyor,
            IAiCommandHandler commandHandler,
            IAiCommentaryService commentary,
            GameEventExtractor extractor,
            GameSessionState session)
            : base(conveyor)
        {
            _commandHandler = commandHandler;
            _commentary = commentary;
            _extractor = extractor;
            _session = session;
        }

        public override IEnumerable<int> HandledMessageTypes =>
            Enumerable.Repeat(BuiltInMessageTypes.TextMessage, 1);

        public override IEnumerable<int> HandledCommandTypes =>
            Enumerable.Repeat(BuiltInCommandTypes.TextCommand, 1);

        public override void HandleCommand(Command command, bool isImport = false)
        {
            if (command is TextCommand tc)
            {
                string text = tc.CommandText?.Trim() ?? string.Empty;
                _session.LastPlayerCommand = text;
                if (_commandHandler.TryHandle(text))
                {
                    command.Handled = true;
                    return;
                }
            }
        }

        public override void HandleMessage(Message message, CancellationToken ct = default)
        {
            if (message is OutputToMainWindowMessage omw)
            {
                string plain = omw.InnerText;
                if (string.IsNullOrWhiteSpace(plain)) return;

                // Keep last 30 lines for context
                if (_session.RecentLines.Count >= 30) _session.RecentLines.RemoveAt(0);
                _session.RecentLines.Add(plain);

                var ev = _extractor.TryExtract(plain, _session);
                if (ev != null)
                {
                    _session.RecentImportantEvents.Add(ev);
                    if (_session.RecentImportantEvents.Count > 50)
                        _session.RecentImportantEvents.RemoveAt(0);
                    _commentary?.OnGameEvent(ev, _session);
                }
            }
        }
    }
}
```

- [ ] Создать `Commands/AiCommandHandler.cs` (заготовка — полная реализация в Этапе C+):

```csharp
using System;
using System.Windows;
using Adan.Client.Common.Conveyor;
using Adan.Client.Common.Messages;
using Adan.Client.Common.Themes;
using Adan.Client.Plugins.AI.Abstractions;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Commands
{
    public class AiCommandHandler : IAiCommandHandler
    {
        private const string Prefix = "/ai";
        private readonly ILocalLlmService _llm;
        private readonly IAiContextBuilder _context;
        private readonly IGameMemoryService _memory;
        private readonly ILoreSearchService _lore;
        private readonly GameSessionState _session;
        private readonly AiSettings _settings;
        private readonly MessageConveyor _conveyor;

        public AiCommandHandler(
            ILocalLlmService llm,
            IAiContextBuilder context,
            IGameMemoryService memory,
            ILoreSearchService lore,
            GameSessionState session,
            AiSettings settings,
            MessageConveyor conveyor)
        {
            _llm = llm; _context = context; _memory = memory;
            _lore = lore; _session = session; _settings = settings;
            _conveyor = conveyor;
        }

        public bool TryHandle(string commandText)
        {
            if (!commandText.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
            string args = commandText.Length > Prefix.Length
                ? commandText.Substring(Prefix.Length).Trim()
                : string.Empty;
            HandleAsync(args);
            return true;
        }

        private async void HandleAsync(string args)
        {
            try
            {
                if (args == "on") { _settings.Enabled = true; AiSettingsSerializer.Save(_settings); Output("AI включён."); return; }
                if (args == "off") { _settings.Enabled = false; AiSettingsSerializer.Save(_settings); Output("AI выключён."); return; }
                if (args == "status") { Output($"Статус: {_llm.Status}"); return; }
                if (args == "cancel") { _llm.CancelCurrent(); return; }
                if (args.StartsWith("remember "))
                {
                    string note = args.Substring(9);
                    _memory.SaveUserNote(note, _session.CurrentZoneId > 0 ? _session.CurrentZoneId : (long?)null,
                        _session.CurrentRoomId > 0 ? _session.CurrentRoomId : (long?)null);
                    Output("Заметка сохранена.");
                    return;
                }
                if (string.IsNullOrEmpty(args)) { Output($"Привет! Я {_settings.AssistantName}. Задай вопрос: /ai <вопрос>"); return; }

                if (_llm.Status != LlmStatus.Ready)
                {
                    Output($"[{_settings.AssistantName}]: Модель не загружена (статус: {_llm.Status}). Загружаю...");
                    await _llm.LoadModelAsync();
                    if (_llm.Status != LlmStatus.Ready) { Output("Не удалось загрузить модель."); return; }
                }

                string prompt = _context.BuildPrompt(args, _session);
                string answer = await _llm.GenerateAsync(prompt);
                Output($"[{_settings.AssistantName}]: {answer}");
            }
            catch (Exception ex)
            {
                Output($"[AI Error]: {ex.Message}");
            }
        }

        private void Output(string text) =>
            _conveyor.PushMessage(new OutputToMainWindowMessage(text, TextColor.BrightCyan));
    }
}
```

- [ ] Обновить `AiPlugin.cs` — подключить юнит в `InitializeConveyor`:

```csharp
// добавить в InitializeConveyor после создания _memory:
var session = new GameSessionState();
var extractor = new GameEventExtractor();  // будет создан в Этапе C
var lore = new LoreSearchService(_settings, _memory);
var contextBuilder = new AiContextBuilder(_settings, _memory, lore);
_llm = new LocalLlmService(_settings);
var commandHandler = new AiCommandHandler(_llm, contextBuilder, _memory, lore, session, _settings, conveyor);
var commentary = new AiCommentaryService(_settings, _llm, contextBuilder, conveyor, session);
conveyor.AddConveyorUnit(new AiConveyorUnit(conveyor, commandHandler, commentary, extractor, session));
```

- [ ] Собрать.

---

## ЭТАП C: Извлечение событий и карта

### Task C1: IGameEventRule + правила

**Files:**
- Create: `Adan.Client.Plugins.AI/Events/IGameEventRule.cs`
- Create: `Adan.Client.Plugins.AI/Events/GameEventExtractor.cs`
- Create: `Adan.Client.Plugins.AI/Events/Rules/RoomEnteredRule.cs`
- Create: `Adan.Client.Plugins.AI/Events/Rules/MobSeenRule.cs`
- Create: `Adan.Client.Plugins.AI/Events/Rules/MobKilledRule.cs`
- Create: `Adan.Client.Plugins.AI/Events/Rules/ItemPickedUpRule.cs`
- Create: `Adan.Client.Plugins.AI/Events/Rules/MovementRule.cs`
- Test: `Adan.Client.Plugins.AI.Tests/Events/GameEventExtractorTests.cs`

- [ ] Создать `Events/IGameEventRule.cs`:

```csharp
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Events
{
    public interface IGameEventRule
    {
        int Priority { get; }
        bool TryMatch(string line, GameSessionState state, out GameEvent gameEvent);
    }
}
```

- [ ] Создать `Events/Rules/RoomEnteredRule.cs`:

```csharp
using System.Text.RegularExpressions;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Events.Rules
{
    public class RoomEnteredRule : IGameEventRule
    {
        // Типичный паттерн: строка начинается с заглавной и не содержит глаголов боя
        // Адамант: название комнаты — первая строка после пустой, короткая
        private static readonly Regex RoomNamePattern = new Regex(
            @"^[А-ЯЁA-Z][А-ЯЁA-Za-zа-яё0-9\s\-,']{3,60}$",
            RegexOptions.Compiled);

        public int Priority => 10;

        public bool TryMatch(string line, GameSessionState state, out GameEvent gameEvent)
        {
            gameEvent = null;
            string trimmed = line.Trim();
            if (trimmed.Length < 4 || trimmed.Length > 80) return false;
            if (!RoomNamePattern.IsMatch(trimmed)) return false;
            // Не трактуем строки с глаголами боя как название комнаты
            if (trimmed.Contains("убил") || trimmed.Contains("атакует") || trimmed.Contains("нападает")) return false;

            gameEvent = new GameEvent
            {
                Type = GameEventType.RoomEntered,
                EntityName = trimmed,
                RawText = line,
                Importance = 3
            };
            return true;
        }
    }
}
```

- [ ] Создать `Events/Rules/MobSeenRule.cs`:

```csharp
using System.Text.RegularExpressions;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Events.Rules
{
    public class MobSeenRule : IGameEventRule
    {
        private static readonly Regex Pattern = new Regex(
            @"(?:Здесь|Тут)\s+(?:стоит|бродит|лежит|сидит|находится)\s+(.+?)[\.,]?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public int Priority => 20;

        public bool TryMatch(string line, GameSessionState state, out GameEvent gameEvent)
        {
            gameEvent = null;
            var m = Pattern.Match(line.Trim());
            if (!m.Success) return false;
            gameEvent = new GameEvent
            {
                Type = GameEventType.MobSeen,
                EntityName = m.Groups[1].Value.Trim(),
                RawText = line,
                Importance = 2
            };
            return true;
        }
    }
}
```

- [ ] Создать `Events/Rules/MobKilledRule.cs`:

```csharp
using System.Text.RegularExpressions;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Events.Rules
{
    public class MobKilledRule : IGameEventRule
    {
        private static readonly Regex Pattern = new Regex(
            @"(.+?)\s+(?:убит[аo]?|мертв[аo]?|повержен[аo]?|пал[аo]?)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public int Priority => 15;

        public bool TryMatch(string line, GameSessionState state, out GameEvent gameEvent)
        {
            gameEvent = null;
            var m = Pattern.Match(line.Trim());
            if (!m.Success) return false;
            gameEvent = new GameEvent
            {
                Type = GameEventType.MobKilled,
                EntityName = m.Groups[1].Value.Trim(),
                RawText = line,
                Importance = 4
            };
            return true;
        }
    }
}
```

- [ ] Создать `Events/Rules/ItemPickedUpRule.cs`:

```csharp
using System.Text.RegularExpressions;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Events.Rules
{
    public class ItemPickedUpRule : IGameEventRule
    {
        private static readonly Regex Pattern = new Regex(
            @"(?:Вы взяли|Ты подобрал[аи]?|взял[аи]?)\s+(.+?)[\.,]?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public int Priority => 20;

        public bool TryMatch(string line, GameSessionState state, out GameEvent gameEvent)
        {
            gameEvent = null;
            var m = Pattern.Match(line.Trim());
            if (!m.Success) return false;
            gameEvent = new GameEvent
            {
                Type = GameEventType.ItemPickedUp,
                EntityName = m.Groups[1].Value.Trim(),
                RawText = line,
                Importance = 3
            };
            return true;
        }
    }
}
```

- [ ] Создать `Events/Rules/MovementRule.cs`:

```csharp
using System.Collections.Generic;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Events.Rules
{
    public class MovementRule : IGameEventRule
    {
        private static readonly HashSet<string> Directions = new HashSet<string>(
            System.StringComparer.OrdinalIgnoreCase)
        {
            "n","north","север","с",
            "s","south","юг","ю",
            "e","east","восток","в",
            "w","west","запад","з",
            "u","up","вверх","вв",
            "d","down","вниз","вн",
            "ne","nw","se","sw","св","сз","юв","юз"
        };

        public int Priority => 5;

        public bool TryMatch(string line, GameSessionState state, out GameEvent gameEvent)
        {
            gameEvent = null;
            string cmd = state.LastPlayerCommand?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!Directions.Contains(cmd)) return false;
            gameEvent = new GameEvent
            {
                Type = GameEventType.PlayerMoved,
                Direction = cmd,
                RawText = line,
                Importance = 1
            };
            return true;
        }
    }
}
```

- [ ] Создать `Events/GameEventExtractor.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Adan.Client.Plugins.AI.Events.Rules;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Events
{
    public class GameEventExtractor
    {
        private readonly IList<IGameEventRule> _rules;

        public GameEventExtractor() : this(DefaultRules()) { }

        public GameEventExtractor(IList<IGameEventRule> rules)
        {
            _rules = rules.OrderBy(r => r.Priority).ToList();
        }

        public GameEvent TryExtract(string line, GameSessionState state)
        {
            foreach (var rule in _rules)
                if (rule.TryMatch(line, state, out var ev))
                    return ev;
            return null;
        }

        private static IList<IGameEventRule> DefaultRules() => new List<IGameEventRule>
        {
            new MovementRule(),
            new RoomEnteredRule(),
            new MobKilledRule(),
            new MobSeenRule(),
            new ItemPickedUpRule(),
        };
    }
}
```

- [ ] Создать `Adan.Client.Plugins.AI.Tests/Events/GameEventExtractorTests.cs`:

```csharp
using NUnit.Framework;
using Adan.Client.Plugins.AI.Events;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Tests.Events
{
    [TestFixture]
    public class GameEventExtractorTests
    {
        private GameEventExtractor _extractor;
        private GameSessionState _state;

        [SetUp]
        public void SetUp()
        {
            _extractor = new GameEventExtractor();
            _state = new GameSessionState();
        }

        [Test]
        public void MobKilledLine_ReturnsMobKilledEvent()
        {
            var ev = _extractor.TryExtract("Тёмный тролль убит.", _state);
            Assert.That(ev, Is.Not.Null);
            Assert.That(ev.Type, Is.EqualTo(GameEventType.MobKilled));
            Assert.That(ev.EntityName, Is.EqualTo("Тёмный тролль"));
        }

        [Test]
        public void MobSeenLine_ReturnsMobSeenEvent()
        {
            var ev = _extractor.TryExtract("Здесь бродит старый тролль.", _state);
            Assert.That(ev, Is.Not.Null);
            Assert.That(ev.Type, Is.EqualTo(GameEventType.MobSeen));
        }

        [Test]
        public void ItemPickedUp_ReturnsItemPickedUpEvent()
        {
            var ev = _extractor.TryExtract("Вы взяли серебряный ключ.", _state);
            Assert.That(ev, Is.Not.Null);
            Assert.That(ev.Type, Is.EqualTo(GameEventType.ItemPickedUp));
            Assert.That(ev.EntityName, Is.EqualTo("серебряный ключ"));
        }

        [Test]
        public void RegularLine_ReturnsNull_WhenNoMatch()
        {
            var ev = _extractor.TryExtract("Ты смотришь вокруг.", _state);
            // Not a mob killed or item pickup line
            Assert.That(ev?.Type, Is.Not.EqualTo(GameEventType.MobKilled));
            Assert.That(ev?.Type, Is.Not.EqualTo(GameEventType.ItemPickedUp));
        }

        [Test]
        public void MovementCommand_WithRoomLine_ReturnsMovedEvent()
        {
            _state.LastPlayerCommand = "n";
            var ev = _extractor.TryExtract("Перекрёсток дорог", _state);
            Assert.That(ev, Is.Not.Null);
            Assert.That(ev.Type, Is.EqualTo(GameEventType.PlayerMoved));
        }
    }
}
```

- [ ] Запустить тесты, убедиться что проходят.
- [ ] Commit: `feat(ai): Этап C — правила извлечения игровых событий`

---

## ЭТАП D: Лор — индексация и поиск

### Task D1: LoreIndexer

**Files:**
- Create: `Adan.Client.Plugins.AI/Lore/LoreIndexer.cs`
- Create: `Adan.Client.Plugins.AI/Lore/LoreSearchService.cs`
- Test: `Adan.Client.Plugins.AI.Tests/Lore/LoreIndexerTests.cs`

- [ ] Создать `Lore/LoreIndexer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Adan.Client.Plugins.AI.Abstractions;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Lore
{
    public class LoreIndexer
    {
        private const int ChunkSize = 800;
        private const long MaxFileBytes = 5 * 1024 * 1024; // 5 MB
        private static readonly string[] SupportedExtensions = { ".txt", ".md", ".json" };

        private readonly AiSettings _settings;
        private readonly IGameMemoryService _memory;

        public LoreIndexer(AiSettings settings, IGameMemoryService memory)
        {
            _settings = settings; _memory = memory;
        }

        public void ReindexAll()
        {
            string dir = _settings.ResolvedLoreDirectory;
            if (!Directory.Exists(dir)) return;
            foreach (string file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (Array.IndexOf(SupportedExtensions, ext) < 0) continue;
                var info = new FileInfo(file);
                if (info.Length > MaxFileBytes) continue;
                IndexFile(file);
            }
        }

        private void IndexFile(string path)
        {
            try
            {
                string content = File.ReadAllText(path, Encoding.UTF8);
                long hash = ComputeHash(content);
                if (!_memory.LoreDocumentChanged(path, hash)) return;

                // Delete old chunks first (no direct API, we trust SaveLoreDocument to replace)
                string title = Path.GetFileNameWithoutExtension(path);
                _memory.SaveLoreDocument(path, title, hash);

                var chunks = SplitIntoChunks(content);
                for (int i = 0; i < chunks.Count; i++)
                    _memory.SaveLoreChunk(path, i, chunks[i].Text, chunks[i].Section);
            }
            catch { /* skip broken files */ }
        }

        private static List<(string Text, string Section)> SplitIntoChunks(string content)
        {
            var result = new List<(string, string)>();
            string currentSection = null;
            var sb = new StringBuilder();

            foreach (string line in content.Split('\n'))
            {
                string trimmed = line.TrimEnd();
                if (trimmed.StartsWith("#") || trimmed.StartsWith("=="))
                    currentSection = trimmed.TrimStart('#', '=', ' ');

                sb.AppendLine(trimmed);
                if (sb.Length >= ChunkSize)
                {
                    result.Add((sb.ToString().Trim(), currentSection));
                    sb.Clear();
                }
            }
            if (sb.Length > 0)
                result.Add((sb.ToString().Trim(), currentSection));
            return result;
        }

        private static long ComputeHash(string s)
        {
            long hash = 0;
            foreach (char c in s) hash = hash * 31 + c;
            return hash;
        }
    }
}
```

- [ ] Создать `Lore/LoreSearchService.cs`:

```csharp
using System.Collections.Generic;
using Adan.Client.Plugins.AI.Abstractions;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Lore
{
    public class LoreSearchService : ILoreSearchService
    {
        private readonly LoreIndexer _indexer;
        private readonly IGameMemoryService _memory;

        public LoreSearchService(AiSettings settings, IGameMemoryService memory)
        {
            _indexer = new LoreIndexer(settings, memory);
            _memory = memory;
        }

        public void ReindexAll() => _indexer.ReindexAll();

        public IList<LoreChunkRecord> Search(string query, string zoneName = null, string roomName = null, int limit = 5)
        {
            string q = BuildQuery(query, zoneName, roomName);
            return _memory.SearchLore(q, limit);
        }

        private static string BuildQuery(string userQuery, string zone, string room)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(userQuery)) parts.Add(userQuery);
            if (!string.IsNullOrWhiteSpace(zone)) parts.Add(zone);
            if (!string.IsNullOrWhiteSpace(room)) parts.Add(room);
            return string.Join(" ", parts);
        }
    }
}
```

- [ ] Создать тест `Lore/LoreIndexerTests.cs`:

```csharp
using System.IO;
using NUnit.Framework;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Lore;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Tests.Lore
{
    [TestFixture]
    public class LoreIndexerTests
    {
        private string _dbPath;
        private string _loreDir;
        private GameMemoryService _memory;
        private LoreIndexer _indexer;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.GetTempFileName() + ".db";
            _loreDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_loreDir);
            var settings = new AiSettings { DatabasePath = _dbPath, LoreDirectory = _loreDir };
            _memory = new GameMemoryService(settings);
            _memory.Initialize();
            _indexer = new LoreIndexer(settings, _memory);
        }

        [TearDown]
        public void TearDown()
        {
            _memory.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_loreDir)) Directory.Delete(_loreDir, true);
        }

        [Test]
        public void ReindexAll_IndexesTextFile()
        {
            File.WriteAllText(Path.Combine(_loreDir, "test.md"), "# Тёмный лес\nЗдесь водятся тролли.");
            _indexer.ReindexAll();
            var results = _memory.SearchLore("тролли", 5);
            Assert.That(results, Is.Not.Empty);
        }

        [Test]
        public void ReindexAll_DoesNotReindexUnchangedFile()
        {
            string path = Path.Combine(_loreDir, "a.txt");
            File.WriteAllText(path, "Контент файла");
            _indexer.ReindexAll();
            _indexer.ReindexAll(); // second call
            // If it re-indexed, there would be duplicate chunks
            var results = _memory.SearchLore("Контент", 10);
            Assert.That(results.Count, Is.LessThanOrEqualTo(2));
        }

        [Test]
        public void ReindexAll_SkipsNonTextFiles()
        {
            File.WriteAllBytes(Path.Combine(_loreDir, "binary.dll"), new byte[] { 0x4D, 0x5A, 0x00 });
            Assert.DoesNotThrow(() => _indexer.ReindexAll());
        }
    }
}
```

- [ ] Запустить тесты. Убедиться что проходят.
- [ ] Commit: `feat(ai): Этап D — индексация и FTS5-поиск лора`

---

## ЭТАП E: Контекст, комментарии, стубы

### Task E1: AiContextBuilder

**Files:**
- Create: `Adan.Client.Plugins.AI/Context/AiContextBuilder.cs`
- Test: `Adan.Client.Plugins.AI.Tests/Context/AiContextBuilderTests.cs`

- [ ] Создать `Context/AiContextBuilder.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using Adan.Client.Plugins.AI.Abstractions;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Context
{
    public class AiContextBuilder : IAiContextBuilder
    {
        private const int MaxContextChars = 3000;
        private readonly AiSettings _settings;
        private readonly IGameMemoryService _memory;
        private readonly ILoreSearchService _lore;

        public AiContextBuilder(AiSettings settings, IGameMemoryService memory, ILoreSearchService lore)
        {
            _settings = settings; _memory = memory; _lore = lore;
        }

        public string BuildPrompt(string userQuestion, GameSessionState session)
        {
            var sb = new StringBuilder();

            // System prompt
            sb.AppendLine($"Ты локальный помощник игрока в текстовой MUD-игре. Тебя зовут {_settings.AssistantName}.");
            sb.AppendLine("Правила: отвечай по-русски, коротко (2-8 предложений), не придумывай маршруты и предметы, которые не видел.");
            sb.AppendLine("Не выводи внутренние рассуждения. Не управляй персонажем без явной команды игрока.");
            sb.AppendLine("/no_think");
            sb.AppendLine();

            // Current state
            if (!string.IsNullOrEmpty(session.CurrentZoneName))
                sb.AppendLine($"[Текущая зона]: {session.CurrentZoneName}");
            if (!string.IsNullOrEmpty(session.CurrentRoomName))
                sb.AppendLine($"[Текущая комната]: {session.CurrentRoomName}");
            if (session.VisibleMobs?.Count > 0)
                sb.AppendLine($"[Мобы здесь]: {string.Join(", ", session.VisibleMobs)}");
            if (session.InCombat)
                sb.AppendLine("[Статус]: В бою");

            // Zone summary
            if (session.CurrentZoneId > 0)
            {
                string summary = _memory.GetZoneSummary(session.CurrentZoneId);
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    sb.AppendLine();
                    sb.AppendLine("[Сводка зоны]:");
                    sb.AppendLine(summary);
                }
            }

            // Recent events (last 5 important)
            var events = _memory.GetRecentEvents(session.CurrentZoneId > 0 ? session.CurrentZoneId : (long?)null, 5);
            if (events.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[Последние события]:");
                foreach (var ev in events)
                    sb.AppendLine($"- {ev.EventType}: {ev.RawText}");
            }

            // Lore search
            var loreChunks = _lore.Search(userQuestion, session.CurrentZoneName, session.CurrentRoomName, 3);
            if (loreChunks.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[Лор]:");
                foreach (var chunk in loreChunks)
                {
                    string section = chunk.SectionTitle != null ? $" ({chunk.SectionTitle})" : "";
                    sb.AppendLine($"[{chunk.DocTitle}{section}]: {chunk.Content}");
                    if (sb.Length > MaxContextChars) break;
                }
            }

            // User notes (top 3)
            // (simplified — user notes are in GameEvents with type UserNote; full impl in later iteration)

            sb.AppendLine();
            sb.AppendLine($"[Вопрос игрока]: {userQuestion}");
            sb.Append($"{_settings.AssistantName}:");

            return sb.ToString();
        }
    }
}
```

- [ ] Создать тест `Context/AiContextBuilderTests.cs`:

```csharp
using System.IO;
using NUnit.Framework;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Context;
using Adan.Client.Plugins.AI.Lore;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Tests.Context
{
    [TestFixture]
    public class AiContextBuilderTests
    {
        private string _dbPath;
        private GameMemoryService _memory;
        private AiContextBuilder _builder;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.GetTempFileName() + ".db";
            var settings = new AiSettings { DatabasePath = _dbPath, LoreDirectory = Path.GetTempPath() };
            _memory = new GameMemoryService(settings);
            _memory.Initialize();
            var lore = new LoreSearchService(settings, _memory);
            _builder = new AiContextBuilder(settings, _memory, lore);
        }

        [TearDown]
        public void TearDown()
        {
            _memory.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Test]
        public void BuildPrompt_ContainsUserQuestion()
        {
            var session = new GameSessionState { CurrentZoneName = "Лес" };
            string prompt = _builder.BuildPrompt("что за место?", session);
            Assert.That(prompt, Does.Contain("что за место?"));
        }

        [Test]
        public void BuildPrompt_ContainsZoneName()
        {
            var session = new GameSessionState { CurrentZoneName = "Тёмный лес" };
            string prompt = _builder.BuildPrompt("где я?", session);
            Assert.That(prompt, Does.Contain("Тёмный лес"));
        }

        [Test]
        public void BuildPrompt_DoesNotExceedReasonableLength()
        {
            var session = new GameSessionState { CurrentZoneName = "Лес" };
            string prompt = _builder.BuildPrompt("тест", session);
            Assert.That(prompt.Length, Is.LessThan(6000));
        }

        [Test]
        public void BuildPrompt_NeverOmitsUserQuestion()
        {
            var session = new GameSessionState();
            // Even with empty context
            string prompt = _builder.BuildPrompt("секретный вопрос", session);
            Assert.That(prompt, Does.Contain("секретный вопрос"));
        }
    }
}
```

---

### Task E2: AiCommentaryService (стуб)

**Files:**
- Create: `Adan.Client.Plugins.AI/Commentary/AiCommentaryService.cs`

- [ ] Создать `Commentary/AiCommentaryService.cs`:

```csharp
using System;
using System.Windows;
using Adan.Client.Common.Conveyor;
using Adan.Client.Common.Messages;
using Adan.Client.Common.Themes;
using Adan.Client.Plugins.AI.Abstractions;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Events;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI.Commentary
{
    public class AiCommentaryService : IAiCommentaryService
    {
        private readonly AiSettings _settings;
        private readonly ILocalLlmService _llm;
        private readonly IAiContextBuilder _context;
        private readonly MessageConveyor _conveyor;
        private DateTime _lastCommentAt = DateTime.MinValue;

        public bool IsEnabled { get; set; } = true;

        public AiCommentaryService(AiSettings settings, ILocalLlmService llm,
            IAiContextBuilder context, MessageConveyor conveyor, GameSessionState session)
        {
            _settings = settings; _llm = llm; _context = context; _conveyor = conveyor;
        }

        public async void OnGameEvent(GameEvent ev, GameSessionState session)
        {
            if (!IsEnabled || !_settings.CommentaryEnabled) return;
            if (_llm.Status != LlmStatus.Ready) return;
            if (ev.Importance < 3) return;
            if ((DateTime.UtcNow - _lastCommentAt).TotalSeconds < _settings.CommentaryCooldownSeconds) return;

            // Check for loop detection: last 6 rooms — if A→B→C→A→B→C, trigger
            if (DetectLoop(session)) { OutputComment($"[{_settings.AssistantName}]: Похоже, мы ходим по кругу."); return; }

            _lastCommentAt = DateTime.UtcNow;
            try
            {
                string trigger = $"Кратко прокомментируй (1-2 предложения): {ev.RawText}";
                string prompt = _context.BuildPrompt(trigger, session);
                string comment = await _llm.GenerateAsync(prompt);
                if (!string.IsNullOrWhiteSpace(comment))
                    OutputComment($"[{_settings.AssistantName}]: {comment}");
            }
            catch { }
        }

        private static bool DetectLoop(GameSessionState session)
        {
            var path = session.RecentRoomPath;
            if (path.Count < 6) return false;
            int half = path.Count / 2;
            for (int i = 0; i < 3; i++)
                if (path[path.Count - 1 - i] != path[path.Count - 1 - i - 3]) return false;
            return true;
        }

        private void OutputComment(string text) =>
            _conveyor.PushMessage(new OutputToMainWindowMessage(text, TextColor.Cyan));
    }
}
```

- [ ] Создать `Fakes/FakeLlmService.cs` для тестов:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Adan.Client.Plugins.AI.Abstractions;

namespace Adan.Client.Plugins.AI.Tests.Fakes
{
    public class FakeLlmService : ILocalLlmService
    {
        public LlmStatus Status { get; private set; } = LlmStatus.Ready;
        public string FakeResponse { get; set; } = "Тестовый ответ";
        public event EventHandler<LlmStatus> StatusChanged;

        public Task LoadModelAsync(CancellationToken ct = default) { Status = LlmStatus.Ready; return Task.CompletedTask; }
        public void UnloadModel() { Status = LlmStatus.Disabled; }
        public Task<string> GenerateAsync(string prompt, CancellationToken ct = default) => Task.FromResult(FakeResponse);
        public void CancelCurrent() { }
        public void Dispose() { }
    }
}
```

- [ ] Собрать, запустить все тесты: `dotnet test Adan.Client.Plugins.AI.Tests\`
- [ ] Commit: `feat(ai): Этап E — AiContextBuilder, commentary, FakeLlm`

---

## ЭТАП F: Финализация

### Task F1: Обновить AiPlugin.cs — полная сборка компонентов

- [ ] В `AiPlugin.cs` соединить все компоненты (заменить заглушки на реальные классы):

```csharp
using System.ComponentModel.Composition;
using System.Windows;
using Adan.Client.Common.Conveyor;
using Adan.Client.Common.Model;
using Adan.Client.Common.Plugins;
using Adan.Client.Common.ViewModel;
using Adan.Client.Plugins.AI.Commands;
using Adan.Client.Plugins.AI.Commentary;
using Adan.Client.Plugins.AI.Configuration;
using Adan.Client.Plugins.AI.Context;
using Adan.Client.Plugins.AI.Events;
using Adan.Client.Plugins.AI.Inference;
using Adan.Client.Plugins.AI.Lore;
using Adan.Client.Plugins.AI.Memory;

namespace Adan.Client.Plugins.AI
{
    [Export(typeof(PluginBase))]
    public sealed class AiPlugin : PluginBase
    {
        private AiSettings _settings;
        private GameMemoryService _memory;
        private LocalLlmService _llm;

        public override string Name => "LocalAI";

        public override void InitializeConveyor(MessageConveyor conveyor)
        {
            _settings = AiSettingsSerializer.Load();
            if (!_settings.Enabled) return;

            _memory = new GameMemoryService(_settings);
            try { _memory.Initialize(); } catch { }

            var session = new GameSessionState();
            var extractor = new GameEventExtractor();
            var lore = new LoreSearchService(_settings, _memory);
            var contextBuilder = new AiContextBuilder(_settings, _memory, lore);
            _llm = new LocalLlmService(_settings);
            var commandHandler = new AiCommandHandler(_llm, contextBuilder, _memory, lore, session, _settings, conveyor);
            var commentary = new AiCommentaryService(_settings, _llm, contextBuilder, conveyor, session);
            conveyor.AddConveyorUnit(new AiConveyorUnit(conveyor, commandHandler, commentary, extractor, session));

            // Async lore reindex on startup
            System.Threading.Tasks.Task.Run(() => lore.ReindexAll());
        }

        public override void Initialize(InitializationStatusModel initializationStatusModel, Window mainWindow)
        {
            initializationStatusModel.CurrentPluginName = "Local AI";
            initializationStatusModel.PluginInitializationStatus = "OK";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _llm?.Dispose(); _memory?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
```

---

### Task F2: .gitignore и документация

**Files:**
- Modify: `.gitignore`
- Create: `docs/local-ai.md`

- [ ] Добавить в `.gitignore`:

```
*.gguf
Models/
Data/ai-memory.db
Data/ai-memory.db-shm
Data/ai-memory.db-wal
ai-settings.xml
```

- [ ] Создать `docs/local-ai.md` (краткий — полный контент см. в промпте).

- [ ] Финальная сборка всего solution: `msbuild Adan.Client2017.sln`
- [ ] Запустить все тесты: `dotnet test`
- [ ] Commit: `feat(ai): Этап F — финализация, gitignore, документация`

---

## Критерии готовности

- [ ] Solution собирается без ошибок
- [ ] `Adan.Client.Plugins.AI.dll` появляется в папке Plugins
- [ ] `/ai` не отправляется на сервер (command.Handled = true)
- [ ] При `Enabled=false` юнит не регистрируется
- [ ] Все тесты GameMemoryService проходят
- [ ] Все тесты GameEventExtractor проходят
- [ ] Все тесты LoreIndexer проходят
- [ ] Все тесты AiContextBuilder проходят
- [ ] FakeLlmService позволяет тестировать без GGUF-модели
- [ ] Сайдкар `Adan.AI.Host.exe` собирается для .NET 8
