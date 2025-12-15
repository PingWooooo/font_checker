using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoldManagerMvc.Data;
using MoldManagerMvc.Models;

namespace MoldManagerMvc.Controllers
{
    public class MoldController : Controller
    {
        private readonly AppDbContext _context;

        public MoldController(AppDbContext context)
        {
            _context = context;
        }

        // GET: 首頁 (儀表板 + 列表)
        public async Task<IActionResult> Index(string searchTerm)
        {
            var query = _context.Molds.AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(m => m.Name.Contains(searchTerm) || m.Id.Contains(searchTerm) || m.Model.Contains(searchTerm));
            }

            var molds = await query.ToListAsync();

            // 準備 View Data 給 Dashboard 統計使用
            ViewBag.Total = await _context.Molds.CountAsync();
            ViewBag.InUse = await _context.Molds.CountAsync(m => m.Status == "in-use");
            ViewBag.Available = await _context.Molds.CountAsync(m => m.Status == "available");
            ViewBag.Maintenance = await _context.Molds.CountAsync(m => m.Status == "maintenance");

            return View(molds);
        }

        // GET: 歷程記錄
        public async Task<IActionResult> Logs()
        {
            var logs = await _context.MoldLogs.OrderByDescending(l => l.Timestamp).ToListAsync();
            return View(logs);
        }

        // POST: 新增模具
        [HttpPost]
        public async Task<IActionResult> Create(string model, string assetSuffix, string name, int maxShots)
        {
            string uniqueId = $"{model}-{assetSuffix}";

            if (await _context.Molds.AnyAsync(m => m.Id == uniqueId))
            {
                TempData["Error"] = "模具編號已存在";
                return RedirectToAction(nameof(Index));
            }

            var mold = new Mold
            {
                Id = uniqueId,
                Model = model,
                Name = name,
                MaxShots = maxShots,
                Status = "available",
                LastMaintenance = DateTime.Now
            };

            _context.Molds.Add(mold);
            _context.MoldLogs.Add(new MoldLog { MoldId = uniqueId, Action = "create", Note = "建立新模具資料" });
            
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // POST: 領用 (Checkout)
        [HttpPost]
        public async Task<IActionResult> Checkout(string id, string operatorName, string machine)
        {
            var mold = await _context.Molds.FindAsync(id);
            if (mold != null && mold.Status == "available")
            {
                mold.Status = "in-use";
                mold.Operator = operatorName;
                mold.Machine = machine;

                _context.MoldLogs.Add(new MoldLog 
                { 
                    MoldId = id, 
                    Action = "checkout", 
                    Operator = operatorName, 
                    Machine = machine, 
                    Note = "領用出庫" 
                });

                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: 歸還 (Return)
        [HttpPost]
        public async Task<IActionResult> Return(string id, int shotsAdded)
        {
            var mold = await _context.Molds.FindAsync(id);
            if (mold != null && mold.Status == "in-use")
            {
                mold.CurrentShots += shotsAdded;
                
                // 自動判斷是否需要保養
                string nextStatus = "available";
                string note = "歸還入庫";

                if (mold.CurrentShots >= mold.MaxShots)
                {
                    nextStatus = "maintenance";
                    note = "壽命已達上限，自動轉為維修/保養";
                }

                mold.Status = nextStatus;
                mold.Operator = null;
                mold.Machine = null;

                _context.MoldLogs.Add(new MoldLog 
                { 
                    MoldId = id, 
                    Action = "return", 
                    ShotsAdded = shotsAdded, 
                    Note = note 
                });

                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: 完成保養 (Maintain)
        [HttpPost]
        public async Task<IActionResult> Maintain(string id)
        {
            var mold = await _context.Molds.FindAsync(id);
            if (mold != null && mold.Status == "maintenance")
            {
                mold.Status = "available";
                mold.LastMaintenance = DateTime.Now;
                
                // 這裡選擇不歸零壽命，僅做狀態重置，視業務需求而定
                
                _context.MoldLogs.Add(new MoldLog 
                { 
                    MoldId = id, 
                    Action = "maintenance", 
                    Note = "完成保養，重新入庫" 
                });

                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }
        
        // POST: 刪除
        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            var mold = await _context.Molds.FindAsync(id);
            if (mold != null)
            {
                _context.Molds.Remove(mold);
                _context.MoldLogs.Add(new MoldLog { MoldId = id, Action = "delete", Note = "刪除模具" });
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }
    }
}