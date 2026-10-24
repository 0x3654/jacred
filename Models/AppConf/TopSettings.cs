namespace JacRed.Models.AppConf
{
    /// <summary>
    /// Фоновая сборка «топа по популярности» (конец /api/v1.0/top):
    /// шарды, активные за окно <see cref="windowDays"/>, агрегируются в
    /// строки (макс/сумма сидов тайтла, лучшая раздача), эндпоинт отдаёт
    /// из памяти. Полный обход FileDB на запрос запрещён — IO-шторм по
    /// сотням тысяч шардов; поэтому пересборка фоном + кэш агрегатов
    /// по fileTime шарда (неизменный шард не перечитывается).
    /// </summary>
    public class TopSettings
    {
        public bool enable { get; set; } = true;

        /// <summary>Сколько дней шард ещё «живой» (окно популярности).</summary>
        public int windowDays { get; set; } = 7;

        /// <summary>Пересборка топа, минуты.</summary>
        public int refreshMinutes { get; set; } = 15;

        /// <summary>Сколько строк держать в топе.</summary>
        public int keepRows { get; set; } = 5000;

        /// <summary>Бюджет тика: максимум чтений НОВЫХ шардов (кэш не в счёт).</summary>
        public int maxShardsPerTick { get; set; } = 20000;
    }
}
