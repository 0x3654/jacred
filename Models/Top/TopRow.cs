using System;
using System.Collections.Generic;

namespace JacRed.Models.Top
{
    /// <summary>Агрегат всех раздач тайтла (шард masterDb = один тайтл) для топа.</summary>
    public class TopRow
    {
        /// <summary>Ключ masterDb «search_name:search_originalname».</summary>
        public string key { get; set; }

        public string name { get; set; }

        public string originalname { get; set; }

        public int relased { get; set; }

        /// <summary>movie|tv — по лучшей раздаче.</summary>
        public string type { get; set; }

        public int maxSid { get; set; }

        public int sumSid { get; set; }

        public int releases { get; set; }

        public List<string> trackers { get; set; } = new List<string>();

        public DateTime lastUpdate { get; set; }

        /// <summary>Лучшая раздача по сидам — её качество/озвучки/магнит показываются.</summary>
        public TopBest best { get; set; }
    }

    public class TopBest
    {
        public string tracker { get; set; }

        public string url { get; set; }

        public string magnet { get; set; }

        public string title { get; set; }

        public int sid { get; set; }

        public double size { get; set; }

        public string sizeName { get; set; }

        public int quality { get; set; }

        public string videotype { get; set; }

        public List<string> voices { get; set; } = new List<string>();

        public List<int> seasons { get; set; } = new List<int>();

        public DateTime createTime { get; set; }
    }
}
