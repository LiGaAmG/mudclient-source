using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Adan.Client.Plugins.AI.Abstractions;
using Adan.Client.Plugins.AI.Configuration;

namespace Adan.Client.Plugins.AI.Lore
{
    public class LoreIndexer
    {
        private const int MaxChunkChars = 1200;
        private const long MaxFileBytes = 5 * 1024 * 1024;
        private static readonly string[] SupportedExtensions = { ".txt", ".md", ".json" };
        private static readonly Regex MdClean = new Regex(@"\*{1,2}|_{1,2}|`|#{1,6}\s*", RegexOptions.Compiled);
        private static readonly Regex TableRow = new Regex(@"^\|.+\|$", RegexOptions.Compiled);

        private readonly AiSettings _settings;
        private readonly IGameMemoryService _memory;

        public LoreIndexer(AiSettings settings, IGameMemoryService memory)
        {
            _settings = settings;
            _memory = memory;
        }

        public ReindexResult ReindexAll()
        {
            string dir = _settings.ResolvedLoreDirectory;
            if (!Directory.Exists(dir))
                return new ReindexResult(0, 0, 0);

            int scanned = 0, updated = 0, chunks = 0;
            foreach (string file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (Array.IndexOf(SupportedExtensions, ext) < 0) continue;
                if (new FileInfo(file).Length > MaxFileBytes) continue;
                scanned++;
                int c = IndexFile(file);
                if (c >= 0) { updated++; chunks += c; }
            }
            return new ReindexResult(scanned, updated, chunks);
        }

        private int IndexFile(string path)
        {
            try
            {
                string content = File.ReadAllText(path, Encoding.UTF8);
                long hash = ComputeHash(content);
                if (!_memory.LoreDocumentChanged(path, hash)) return -1;

                string title = Path.GetFileNameWithoutExtension(path);
                _memory.SaveLoreDocument(path, title, hash);

                var chunks = SplitIntoSections(content);
                chunks.AddRange(BuildEntityCards(content));
                for (int i = 0; i < chunks.Count; i++)
                    _memory.SaveLoreChunk(path, i, chunks[i].Content, chunks[i].Section);
                return chunks.Count;
            }
            catch { return -1; }
        }

        // Нарезаем по ## заголовкам — каждая секция отдельный чанк
        public static List<ChunkInfo> SplitIntoSections(string content)
        {
            var result = new List<ChunkInfo>();
            var lines = content.Split('\n');
            string currentSection = null;
            string currentH2 = null;
            var sb = new StringBuilder();

            foreach (string rawLine in lines)
            {
                string line = rawLine.TrimEnd();

                // Новый раздел — сохраняем предыдущий. ### получает префикс родительского ##,
                // чтобы чанк "Лут — Броня" находился по слову "лут"
                if (line.StartsWith("# "))
                {
                    FlushSection(sb, currentSection, result);
                    currentH2 = line.TrimStart('#', ' ');
                    currentSection = currentH2;
                    continue;
                }
                if (line.StartsWith("## ") && !line.StartsWith("###"))
                {
                    FlushSection(sb, currentSection, result);
                    currentH2 = line.TrimStart('#', ' ');
                    currentSection = currentH2;
                    continue;
                }
                if (line.StartsWith("### "))
                {
                    FlushSection(sb, currentSection, result);
                    string h3 = line.TrimStart('#', ' ');
                    currentSection = string.IsNullOrEmpty(currentH2) ? h3 : currentH2 + " — " + h3;
                    continue;
                }

                // Пропускаем разделители и пустые строки подряд
                if (line == "---" || line == "***") continue;

                // Строки таблицы — только ячейки значений, не заголовки
                if (TableRow.IsMatch(line))
                {
                    if (line.Contains("---")) continue; // разделитель таблицы
                    // Берём только ячейки (убираем | обрамление)
                    string row = line.Trim('|').Replace("|", " — ");
                    sb.AppendLine(CleanMarkdown(row));
                    continue;
                }

                string cleaned = CleanMarkdown(line);
                if (!string.IsNullOrWhiteSpace(cleaned))
                    sb.AppendLine(cleaned);

                // Если секция стала слишком большой — разбиваем
                if (sb.Length > MaxChunkChars)
                {
                    FlushSection(sb, currentSection, result);
                }
            }

            FlushSection(sb, currentSection, result);
            return result;
        }

        /// <summary>
        /// Backwards-compatible entry point for callers that do not care
        /// whether chunks originated from Markdown sections or plain text.
        /// </summary>
        public static List<ChunkInfo> SplitIntoChunks(string content)
        {
            return SplitIntoSections(content);
        }

        private static readonly Regex BoldEntity = new Regex(@"\*\*([^*\n]{3,60}?)\*\*", RegexOptions.Compiled);

        // Карточки сущностей: всё жирное в md — моб или предмет. Для каждого
        // собираем все строки файла где он упомянут (описание, лут, советы) —
        // вопрос "что знаешь про X" получает одну карточку вместо кусков из трёх секций.
        public static List<ChunkInfo> BuildEntityCards(string content)
        {
            var cards = new List<ChunkInfo>();
            var lines = content.Split((char)10);

            var entities = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in BoldEntity.Matches(content))
            {
                string name = m.Groups[1].Value.Trim().Trim((char)171, (char)187, (char)34).TrimEnd((char)58, (char)46, (char)44).Trim();
                if (name.Length < 3) continue;
                if (seen.Add(name)) entities.Add(name);
            }

            foreach (string ent in entities)
            {
                string stem = EntityStem(ent);
                var sb = new StringBuilder();
                var used = new HashSet<string>();
                bool prevMatched = false;

                foreach (string rawLine in lines)
                {
                    string line = rawLine.TrimEnd();
                    if (line.Length == 0 || line == "---" || line.StartsWith("#")) { prevMatched = false; continue; }

                    bool matched = line.IndexOf(stem, StringComparison.OrdinalIgnoreCase) >= 0;
                    // Продолжение предложения с прошлой строки (начинается с маленькой буквы/цифры/скобки)
                    bool continuation = prevMatched && !matched && line.Length > 0 &&
                        (char.IsLower(line.TrimStart()[0]) || char.IsDigit(line.TrimStart()[0]) || line.TrimStart()[0] == (char)126 || line.TrimStart()[0] == (char)40);
                    prevMatched = matched;
                    if (!matched && !continuation) continue;

                    string cleaned;
                    if (TableRow.IsMatch(line))
                    {
                        if (line.Contains("---")) continue;
                        cleaned = CleanMarkdown(line.Trim((char)124).Replace("|", " — "));
                    }
                    else cleaned = CleanMarkdown(line);

                    if (string.IsNullOrWhiteSpace(cleaned) || !used.Add(cleaned)) continue;
                    sb.AppendLine(cleaned);
                    if (sb.Length > 900) break;
                }

                string text = sb.ToString().Trim();
                // Карточка полезна только если собрала 2+ строки (иначе дублирует секцию)
                if (used.Count >= 2)
                    cards.Add(new ChunkInfo { Content = text, Section = ent });
            }
            return cards;
        }

        // Самое длинное слово имени без окончания — ловит склонения ("Настоятель" -> "настояте")
        private static string EntityStem(string entity)
        {
            string best = entity;
            foreach (string w in entity.Split((char)32, (char)45))
                if (w.Length >= 4 && (best == entity || w.Length > best.Length)) best = w;
            if (best == entity && entity.Length > 5) return entity.Substring(0, entity.Length - 2);
            return best.Length > 5 ? best.Substring(0, best.Length - 2) : best;
        }

        private static void FlushSection(StringBuilder sb, string section, List<ChunkInfo> result)
        {
            string text = sb.ToString().Trim();
            sb.Clear();
            while (!string.IsNullOrWhiteSpace(text))
            {
                if (text.Length <= MaxChunkChars)
                {
                    result.Add(new ChunkInfo { Content = text, Section = section });
                    return;
                }

                int splitAt = text.LastIndexOfAny(new[] { ' ', '\n', '\r', '\t' }, MaxChunkChars - 1);
                if (splitAt <= 0)
                    splitAt = MaxChunkChars;

                result.Add(new ChunkInfo { Content = text.Substring(0, splitAt).Trim(), Section = section });
                text = text.Substring(splitAt).Trim();
            }
        }

        private static string CleanMarkdown(string line)
        {
            return MdClean.Replace(line, "").Trim();
        }

        // Менять при изменении логики нарезки — форсит переиндексацию без правки файлов
        private const long IndexerVersion = 4;

        private static long ComputeHash(string s)
        {
            long hash = 17 + IndexerVersion * 1000003;
            foreach (char c in s) hash = hash * 31 + c;
            return hash;
        }

        public struct ChunkInfo { public string Content; public string Section; }
    }

    public struct ReindexResult
    {
        public int Scanned, Updated, Chunks;
        public ReindexResult(int s, int u, int c) { Scanned = s; Updated = u; Chunks = c; }
    }
}
