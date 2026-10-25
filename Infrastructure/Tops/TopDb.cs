using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Indexers;
using JacRed.Models.Details;
using JacRed.Models.Top;

namespace JacRed.Infrastructure.Tops
{
    /// <summary>
    /// «Топ по популярности»: агрегаты активных шардов masterDb в памяти,
    /// /api/v1.0/top отдаёт без обращения к дискам. Пересборка фоном
    /// (TopWorker): шард перечитывается только при смене fileTime,
    /// поэтому steady-state тик почти без IO.
    /// </summary>
    public static class TopDb
    {
        const string TopPath = "Data/temp/top.json";

        static readonly object _lock = new object();
        static List<TopRow> _rows = new List<TopRow>();
        static DateTime _updatedAt;

        class CachedRow
        {
            public long fileTime;
            public TopRow row; // null — шард читали, тайтлов без раздач не оставляем
        }

        // key → агрегат последнего прочитанного состояния шарда
        static Dictionary<string, CachedRow> _agg = new Dictionary<string, CachedRow>();

        static TopDb()
        {
            try
            {
                if (File.Exists(TopPath))
                {
                    var dump = JsonStream.Read<TopDump>(TopPath);
                    if (dump?.rows != null)
                    {
                        _rows = dump.rows;
                        _updatedAt = dump.updatedAt;
                    }
                }
            }
            catch { }
        }

        #region Build

        public static async Task BuildAsync(CancellationToken cancellationToken = default)
        {
            var conf = AppInit.conf?.top;
            if (conf == null || !conf.enable)
                return;

            var started = DateTime.Now;
            var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, conf.windowDays));

            var shards = FileDB.masterDb.ToArray()
                .Where(i => i.Value.updateTime >= cutoff)
                .OrderByDescending(i => i.Value.updateTime)
                .ToList();

            int reads = 0;
            var next = new Dictionary<string, CachedRow>(shards.Count);
            var rows = new List<TopRow>(shards.Count);

            foreach (var s in shards)
            {
                CachedRow cr;

                if (_agg.TryGetValue(s.Key, out CachedRow old) && old.fileTime == s.Value.fileTime)
                {
                    cr = old;
                }
                else
                {
                    // бюджет чтений: новые шарды сверх лимита пропускаем —
                    // в кэш не кладём, следующий тик дочитает
                    if (reads >= conf.maxShardsPerTick)
                        continue;

                    // кооперативный рёбилд: пауза каждые 100 чтений, иначе
                    // плотный цикл декомпрессии душит поисковый API на слабом VPS
                    // (torznab-поиски падали в 20с-таймаут)
                    if (reads > 0 && reads % 100 == 0)
                        await Task.Delay(50, cancellationToken);

                    reads++;
                    cr = new CachedRow { fileTime = s.Value.fileTime, row = Aggregate(s.Key, FileDB.OpenRead(s.Key, cache: false)) };
                }

                next[s.Key] = cr;
                if (cr.row != null)
                    rows.Add(cr.row);
            }

            _agg = next;

            rows.Sort((a, b) => a.maxSid != b.maxSid ? b.maxSid.CompareTo(a.maxSid) : b.sumSid.CompareTo(a.sumSid));
            if (rows.Count > conf.keepRows)
                rows = rows.GetRange(0, conf.keepRows);

            lock (_lock)
            {
                _rows = rows;
                _updatedAt = DateTime.Now;
            }

            Persist(rows);

            JacRedLog.Information(JacRedLogCategories.Top,
                $"rebuild end / {DateTime.Now:yyyy-MM-dd HH:mm:ss} rows={rows.Count} shards={shards.Count} reads={reads} {(DateTime.Now - started).TotalSeconds:0.#}s");
        }

        // шард = все раздачи одного тайтла: лучшая раздача по сидам задаёт
        // имя/год/тип/качество, трекеры и суммы — по всем раздачам
        static TopRow Aggregate(string key, IReadOnlyDictionary<string, TorrentDetails> db)
        {
            if (db == null || db.Count == 0)
                return null;

            TorrentDetails best = null;
            var trackers = new HashSet<string>();
            int sumSid = 0, count = 0;
            DateTime last = DateTime.MinValue;

            foreach (var t in db.Values)
            {
                if (t?.types == null || t.types.Length == 0)
                    continue;

                count++;
                trackers.Add(t.trackerName);
                if (t.sid > 0)
                    sumSid += t.sid;
                if (t.updateTime > last)
                    last = t.updateTime;

                if (best == null || (t.sid > best.sid))
                    best = t;
            }

            if (best == null || count == 0)
                return null;

            return new TopRow
            {
                key = key,
                name = best.name,
                originalname = best.originalname,
                relased = best.relased,
                type = BestType(best),
                maxSid = best.sid,
                sumSid = sumSid,
                releases = count,
                trackers = trackers.OrderBy(i => i).ToList(),
                lastUpdate = last,
                best = new TopBest
                {
                    tracker = best.trackerName,
                    url = best.url != null && best.url.StartsWith("http") ? best.url : null,
                    magnet = best.magnet,
                    title = best.title,
                    sid = best.sid,
                    size = best.size,
                    sizeName = best.sizeName,
                    quality = best.quality,
                    videotype = best.videotype,
                    voices = best.voices != null ? best.voices.OrderBy(i => i).ToList() : new List<string>(),
                    seasons = best.seasons != null ? best.seasons.OrderBy(i => i).ToList() : new List<int>(),
                    createTime = best.createTime
                }
            };
        }

        static string BestType(TorrentDetails t)
        {
            if (t.types.Contains("tv"))
                return "tv";
            if (t.types.Contains("movie"))
                return "movie";
            return t.types.FirstOrDefault();
        }

        static void Persist(List<TopRow> rows)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TopPath));
                JsonStream.Write(TopPath + ".tmp", new TopDump { updatedAt = DateTime.Now, rows = rows });
                File.Move(TopPath + ".tmp", TopPath, overwrite: true);
            }
            catch (Exception ex)
            {
                JacRedLog.Error(JacRedLogCategories.Top, $"persist error / {ex.Message}");
            }
        }

        #endregion

        #region Query

        public static object Query(string sort, string type, string tracker, int take)
        {
            List<TopRow> rows;
            DateTime updatedAt;

            lock (_lock)
            {
                rows = _rows;
                updatedAt = _updatedAt;
            }

            IEnumerable<TopRow> q = rows;

            if (!string.IsNullOrWhiteSpace(type))
                q = q.Where(r => r.type == type);

            if (!string.IsNullOrWhiteSpace(tracker))
            {
                var allowed = TrackerNameMatching.ToAllowSet(TrackerNameMatching.ParseList(tracker));
                if (allowed.Count > 0)
                    q = q.Where(r => r.trackers.Any(t => TrackerNameMatching.Matches(t, allowed)));
            }

            if (sort == "create")
                q = q.OrderByDescending(r => r.best?.createTime ?? DateTime.MinValue);
            else
                q = q.OrderByDescending(r => r.maxSid).ThenByDescending(r => r.sumSid);

            return new
            {
                updatedAt,
                count = rows.Count,
                rows = q.Take(Math.Clamp(take, 1, 2000)).ToList()
            };
        }

        #endregion

        class TopDump
        {
            public DateTime updatedAt { get; set; }
            public List<TopRow> rows { get; set; }
        }
    }
}
