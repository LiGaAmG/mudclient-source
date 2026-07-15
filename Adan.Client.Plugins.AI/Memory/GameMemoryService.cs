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

        // MEF создаёт 4 инстанса плагина — чистку базы делает только первый
        private static bool _retentionDone;
        private static readonly object _retentionLock = new object();

        public GameMemoryService(AiSettings settings)
        {
            _settings = settings;
        }

        public void Initialize()
        {
            string dbPath = _settings.ResolvedDatabasePath;
            string dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            _conn = new SQLiteConnection("Data Source=" + dbPath + ";Version=3;");
            _conn.Open();
            DbSchema.EnsureSchema(_conn);

            // Ретеншн: события старше 7 дней и накопленный мусор (RoomEntered, полные дубли)
            lock (_retentionLock)
            {
                if (_retentionDone) return;
                _retentionDone = true;
            }
            try
            {
                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM GameEvents WHERE Timestamp < @cutoff";
                    cmd.Parameters.AddWithValue("@cutoff", DateTime.UtcNow.AddDays(-7).ToString("O"));
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = "DELETE FROM GameEvents WHERE EventType = 1"; // RoomEntered больше не пишем
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = "DELETE FROM GameEvents WHERE Id NOT IN (SELECT MIN(Id) FROM GameEvents GROUP BY EventType, IFNULL(RawText,''), Timestamp)";
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        private static string Now()
        {
            return DateTime.UtcNow.ToString("O");
        }

        private static string Normalize(string s)
        {
            return (s ?? string.Empty).Trim().ToLowerInvariant();
        }

        public long UpsertZone(string name)
        {
            string norm = Normalize(name);
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "INSERT OR IGNORE INTO Zones(Name, NormalizedName, FirstSeenAt, LastSeenAt) VALUES(@n,@norm,@now,@now)";
                cmd.Parameters.AddWithValue("@n", name);
                cmd.Parameters.AddWithValue("@norm", norm);
                cmd.Parameters.AddWithValue("@now", Now());
                cmd.ExecuteNonQuery();

                cmd.CommandText = "UPDATE Zones SET LastSeenAt=@now WHERE NormalizedName=@norm";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "SELECT Id FROM Zones WHERE NormalizedName=@norm";
                return (long)cmd.ExecuteScalar();
            }
        }

        public long UpsertRoom(long zoneId, string name, string description)
        {
            string hash = Normalize(name);
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "INSERT OR IGNORE INTO Rooms(ZoneId,Name,NormalizedNameHash,Description,FirstSeenAt,LastSeenAt,VisitCount) VALUES(@z,@n,@h,@d,@now,@now,0)";
                cmd.Parameters.AddWithValue("@z", zoneId);
                cmd.Parameters.AddWithValue("@n", name);
                cmd.Parameters.AddWithValue("@h", hash);
                cmd.Parameters.AddWithValue("@d", (object)description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@now", Now());
                cmd.ExecuteNonQuery();

                cmd.CommandText = "UPDATE Rooms SET LastSeenAt=@now, VisitCount=VisitCount+1 WHERE ZoneId=@z AND NormalizedNameHash=@h";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "SELECT Id FROM Rooms WHERE ZoneId=@z AND NormalizedNameHash=@h";
                return (long)cmd.ExecuteScalar();
            }
        }

        public void ConfirmExit(long fromRoomId, string direction, long toRoomId)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "INSERT OR IGNORE INTO RoomExits(FromRoomId,Direction,ToRoomId,IsConfirmed,FirstSeenAt,LastSeenAt) VALUES(@f,@dir,@t,1,@now,@now)";
                cmd.Parameters.AddWithValue("@f", fromRoomId);
                cmd.Parameters.AddWithValue("@dir", direction.ToLowerInvariant());
                cmd.Parameters.AddWithValue("@t", toRoomId);
                cmd.Parameters.AddWithValue("@now", Now());
                cmd.ExecuteNonQuery();

                cmd.CommandText = "UPDATE RoomExits SET ToRoomId=@t, IsConfirmed=1, LastSeenAt=@now WHERE FromRoomId=@f AND Direction=@dir";
                cmd.ExecuteNonQuery();
            }
        }

        public IList<RoomRecord> GetRoomsInZone(long zoneId)
        {
            var list = new List<RoomRecord>();
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id,Name,Description,VisitCount,LastSeenAt,Notes FROM Rooms WHERE ZoneId=@z";
                cmd.Parameters.AddWithValue("@z", zoneId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new RoomRecord
                        {
                            Id = r.GetInt64(0),
                            ZoneId = zoneId,
                            Name = r.GetString(1),
                            Description = r.IsDBNull(2) ? null : r.GetString(2),
                            VisitCount = r.GetInt32(3),
                            LastSeenAt = DateTime.Parse(r.GetString(4)),
                            Notes = r.IsDBNull(5) ? null : r.GetString(5)
                        });
                    }
                }
            }
            return list;
        }

        public IList<ExitRecord> GetExitsFromRoom(long roomId)
        {
            var list = new List<ExitRecord>();
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id,Direction,ToRoomId,IsConfirmed FROM RoomExits WHERE FromRoomId=@r";
                cmd.Parameters.AddWithValue("@r", roomId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new ExitRecord
                        {
                            Id = reader.GetInt64(0),
                            FromRoomId = roomId,
                            Direction = reader.GetString(1),
                            ToRoomId = reader.IsDBNull(2) ? (long?)null : reader.GetInt64(2),
                            IsConfirmed = reader.GetInt32(3) == 1
                        });
                    }
                }
            }
            return list;
        }

        public IList<long> FindShortestPath(long fromRoomId, long toRoomId)
        {
            var visited = new HashSet<long>();
            var queue = new Queue<List<long>>();
            queue.Enqueue(new List<long> { fromRoomId });
            visited.Add(fromRoomId);

            while (queue.Count > 0)
            {
                var path = queue.Dequeue();
                long current = path[path.Count - 1];
                if (current == toRoomId)
                    return path;
                foreach (var exit in GetExitsFromRoom(current))
                {
                    if (exit.ToRoomId.HasValue && !visited.Contains(exit.ToRoomId.Value))
                    {
                        visited.Add(exit.ToRoomId.Value);
                        var newPath = new List<long>(path);
                        newPath.Add(exit.ToRoomId.Value);
                        queue.Enqueue(newPath);
                    }
                }
                if (visited.Count > 5000)
                    break;
            }
            return new List<long>();
        }

        public long UpsertMob(string name, string description)
        {
            string norm = Normalize(name);
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "INSERT OR IGNORE INTO Mobs(Name,NormalizedName,Description,FirstSeenAt,LastSeenAt) VALUES(@n,@norm,@d,@now,@now)";
                cmd.Parameters.AddWithValue("@n", name);
                cmd.Parameters.AddWithValue("@norm", norm);
                cmd.Parameters.AddWithValue("@d", (object)description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@now", Now());
                cmd.ExecuteNonQuery();

                cmd.CommandText = "UPDATE Mobs SET LastSeenAt=@now WHERE NormalizedName=@norm";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "SELECT Id FROM Mobs WHERE NormalizedName=@norm";
                return (long)cmd.ExecuteScalar();
            }
        }

        public long UpsertItem(string name, string description)
        {
            string norm = Normalize(name);
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "INSERT OR IGNORE INTO Items(Name,NormalizedName,Description,FirstSeenAt,LastSeenAt) VALUES(@n,@norm,@d,@now,@now)";
                cmd.Parameters.AddWithValue("@n", name);
                cmd.Parameters.AddWithValue("@norm", norm);
                cmd.Parameters.AddWithValue("@d", (object)description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@now", Now());
                cmd.ExecuteNonQuery();

                cmd.CommandText = "UPDATE Items SET LastSeenAt=@now WHERE NormalizedName=@norm";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "SELECT Id FROM Items WHERE NormalizedName=@norm";
                return (long)cmd.ExecuteScalar();
            }
        }

        public void RecordMobInRoom(long roomId, long mobId)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "INSERT OR REPLACE INTO RoomMobs(RoomId,MobId,LastSeenAt) VALUES(@r,@m,@now)";
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
                cmd.CommandText = "INSERT OR REPLACE INTO RoomItems(RoomId,ItemId,LastSeenAt) VALUES(@r,@i,@now)";
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
                // Дедуп: если такое же событие уже есть за последние 5 секунд — пропускаем
                // (защита от 4 MEF-инстанций, каждая из которых видит одно и то же событие)
                var cutoff = ev.Timestamp.AddSeconds(-5).ToString("O");
                cmd.CommandText = "SELECT COUNT(1) FROM GameEvents WHERE EventType=@et AND RawText=@raw AND Timestamp>=@cutoff";
                cmd.Parameters.AddWithValue("@et", (int)ev.EventType);
                cmd.Parameters.AddWithValue("@raw", (object)ev.RawText ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cutoff", cutoff);
                var count = (long)cmd.ExecuteScalar();
                if (count > 0) { AiLogger.Log("EVENT_SKIP", "дубль: " + ev.EventType + " | " + ev.RawText); return; }

                cmd.CommandText = "INSERT INTO GameEvents(Timestamp,EventType,ZoneId,RoomId,RawText,StructuredDataJson,Importance) VALUES(@ts,@et,@z,@r,@raw,@json,@imp)";
                cmd.Parameters.AddWithValue("@ts", ev.Timestamp.ToString("O"));
                cmd.Parameters.AddWithValue("@z", ev.ZoneId.HasValue ? (object)ev.ZoneId.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@r", ev.RoomId.HasValue ? (object)ev.RoomId.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@json", (object)ev.StructuredDataJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@imp", ev.Importance);
                cmd.ExecuteNonQuery();
                AiLogger.Log("EVENT_SAVE", ev.EventType + " | " + ev.RawText);
            }
        }

        public IList<GameEventRecord> GetRecentEvents(long? zoneId, int limit)
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
                {
                    while (r.Read())
                    {
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
                }
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
                // Переиндексация: старые чанки документа убираем, иначе задвоятся
                cmd.CommandText = "DELETE FROM LoreChunks WHERE DocPath=@p";
                cmd.Parameters.AddWithValue("@p", path);
                cmd.ExecuteNonQuery();
                try { cmd.CommandText = "DELETE FROM LoreChunksFts WHERE DocPath=@p"; cmd.ExecuteNonQuery(); } catch { }
                cmd.Parameters.Clear();

                cmd.CommandText = "INSERT OR REPLACE INTO LoreDocuments(Path,Title,ContentHash,IndexedAt) VALUES(@p,@t,@h,@now)";
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
            long chunkId;
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO LoreChunks(DocPath,ChunkIndex,SectionTitle,Content) VALUES(@p,@i,@s,@c)";
                cmd.Parameters.AddWithValue("@p", docPath);
                cmd.Parameters.AddWithValue("@i", chunkIndex);
                cmd.Parameters.AddWithValue("@s", (object)sectionTitle ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@c", content);
                cmd.ExecuteNonQuery();
                chunkId = _conn.LastInsertRowId;
            }

            using (var cmd = _conn.CreateCommand())
            {
                // FTS5 может отсутствовать в x86 SQLite — не роняем сохранение если нет таблицы
                try
                {
                    string docTitle = Path.GetFileNameWithoutExtension(docPath);
                    cmd.CommandText = "INSERT INTO LoreChunksFts(rowid,DocPath,DocTitle,SectionTitle,Content) VALUES(@rowid,@p,@dt,@s,@c)";
                    cmd.Parameters.AddWithValue("@rowid", chunkId);
                    cmd.Parameters.AddWithValue("@p", docPath);
                    cmd.Parameters.AddWithValue("@dt", docTitle);
                    cmd.Parameters.AddWithValue("@s", (object)sectionTitle ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@c", content);
                    cmd.ExecuteNonQuery();
                }
                catch { }
            }
        }

        // Стем должен стоять в начале слова: "пада" матчит "падает", но не "попадаешь"
        private static bool ContainsStemAtWordStart(string text, string stem)
        {
            int i = 0;
            while ((i = text.IndexOf(stem, i, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                if (i == 0 || !char.IsLetter(text[i - 1])) return true;
                i++;
            }
            return false;
        }

        public IList<LoreChunkRecord> SearchLore(string query, int limit)
        {
            var list = new List<LoreChunkRecord>();
            if (string.IsNullOrWhiteSpace(query))
                return list;
            using (var cmd = _conn.CreateCommand())
            {
                // Очищаем запрос от спецсимволов FTS5
                string safeQuery = query.Replace("\"", "").Replace("*", "").Trim();
                if (string.IsNullOrEmpty(safeQuery))
                    return list;

                cmd.CommandText = @"
                    SELECT lc.DocPath, lc.SectionTitle, lc.Content, bm25(LoreChunksFts) as score
                    FROM LoreChunksFts
                    JOIN LoreChunks lc ON lc.Id = LoreChunksFts.rowid
                    WHERE LoreChunksFts MATCH @q
                    ORDER BY score
                    LIMIT @lim";
                // Берём длиннейшее слово из запроса для префиксного FTS поиска
                string ftsWord = safeQuery;
                var qwords = safeQuery.Split(new char[]{(char)32}, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (var w in qwords)
                    if (w.Length >= 4) ftsWord = w;
                cmd.Parameters.AddWithValue("@q", ftsWord + "*");
                cmd.Parameters.AddWithValue("@lim", limit);
                bool ftsFound = false;
                try
                {
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            ftsFound = true;
                            list.Add(new LoreChunkRecord
                            {
                                DocPath = r.GetString(0),
                                DocTitle = Path.GetFileNameWithoutExtension(r.GetString(0)),
                                SectionTitle = r.IsDBNull(1) ? null : r.GetString(1),
                                Content = r.GetString(2),
                                Score = r.GetDouble(3)
                            });
                        }
                    }
                }
                catch { }
                if (!ftsFound && ftsWord.Length >= 3)
                {
                    using (var cmd2 = _conn.CreateCommand())
                    {
                        // Мультисловный поиск: каждое значимое слово вопроса — отдельный стем.
                        // SQL только отбирает кандидатов (LIKE слеп к регистру кириллицы),
                        // баллы считаем в C#: совпадение в заголовке = 2, в тексте = 1, сумма по словам.
                        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                            "что","как","где","кто","чем","про","для","есть","или","еще","ещё",
                            "знаешь","какой","какая","какие","каком","можно","надо","расскажи","мне" };
                        var stems = new List<string>();
                        foreach (var qw in qwords)
                        {
                            string t = qw.Trim('?', '!', '.', ',', ')', '(', '"');
                            if (t.Length < 3 || stopWords.Contains(t)) continue;
                            stems.Add(t.Length > 5 ? t.Substring(0, t.Length - 2) : t);
                            if (stems.Count >= 4) break;
                        }
                        if (stems.Count == 0)
                            stems.Add(ftsWord.Length > 5 ? ftsWord.Substring(0, ftsWord.Length - 2) : ftsWord);

                        var sqlParts = new List<string>();
                        for (int si = 0; si < stems.Count; si++)
                        {
                            string p = "@s" + si, pu = "@su" + si;
                            sqlParts.Add("(lc.SectionTitle LIKE " + p + " OR lc.Content LIKE " + p +
                                         " OR lc.SectionTitle LIKE " + pu + " OR lc.Content LIKE " + pu + ")");
                            cmd2.Parameters.AddWithValue(p, "%" + stems[si] + "%");
                            string up = char.ToUpperInvariant(stems[si][0]) + stems[si].Substring(1);
                            cmd2.Parameters.AddWithValue(pu, "%" + up + "%");
                        }
                        cmd2.CommandText = "SELECT lc.DocPath, lc.SectionTitle, lc.Content FROM LoreChunks lc WHERE "
                            + string.Join(" OR ", sqlParts.ToArray()) + " LIMIT @lim";
                        cmd2.Parameters.AddWithValue("@lim", limit * 10);
                        var candidates = new List<LoreChunkRecord>();
                        try
                        {
                            using (var r2 = cmd2.ExecuteReader())
                                while (r2.Read())
                                    candidates.Add(new LoreChunkRecord {
                                        DocPath = r2.GetString(0),
                                        DocTitle = System.IO.Path.GetFileNameWithoutExtension(r2.GetString(0)),
                                        SectionTitle = r2.IsDBNull(1) ? null : r2.GetString(1),
                                        Content = r2.GetString(2), Score = 0 });
                        }
                        catch { }
                        foreach (var cand in candidates)
                        {
                            double score = 0;
                            foreach (var st in stems)
                            {
                                bool inTitle = cand.SectionTitle != null && ContainsStemAtWordStart(cand.SectionTitle, st);
                                bool inBody = ContainsStemAtWordStart(cand.Content, st);
                                if (inTitle) score += 2; else if (inBody) score += 1;
                            }
                            cand.Score = score;
                        }
                        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
                        var seenContent = new HashSet<string>();
                        foreach (var cand in candidates)
                        {
                            if (cand.Score <= 0) break;
                            if (!seenContent.Add(cand.Content)) continue;
                            list.Add(cand);
                            if (list.Count >= limit) break;
                        }
                    }
                }
            }
            return list;
        }

        public void Dispose()
        {
            _conn?.Close();
            _conn?.Dispose();
        }
    }
}
