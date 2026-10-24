using JacRed.Infrastructure.Tops;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers
{
    /// <summary>
    /// «Топ по популярности» из агрегатов TopDb (фоновая пересборка, запрос
    /// из памяти — без обхода FileDB). sort=sid (популярное) | create (новинки).
    /// </summary>
    public class TopController : BaseController
    {
        public TopController(IMemoryCache memoryCache) : base(memoryCache) { }

        [HttpGet]
        [Route("/api/v1.0/top")]
        public JsonResult Top(string sort, string type, string tracker, int take = 500)
        {
            return Json(TopDb.Query(sort, type, tracker, take));
        }
    }
}
