using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ShippingTracer.Data;

namespace ShippingTracer.Controllers
{
    public class ShippingInfosController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ShippingInfosController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: ShippingInfos
        public async Task<IActionResult> Index(int? id)
        {
            if(id == null)
            {
                return NotFound();
            }

            SetShippingId(id);
            var applicationDbContext = _context.ShippingInfos
                .Where(x => x.ShippingId == id).Include(s => s.Shipping);

            return View(await applicationDbContext.ToListAsync());
        }

        // GET: ShippingInfos/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var shippingInfo = await _context.ShippingInfos
                .Include(s => s.Shipping)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (shippingInfo == null)
            {
                return NotFound();
            }

            return View(shippingInfo);
        }

        // GET: ShippingInfos/Create
        public IActionResult Create(int? id)
        {
            //ViewData["ShippingId"] = new SelectList(_context.Shippings, "Id", "Id");
            var info = new ShippingInfo { ShippingId = (int)id, CreatedAt = DateTime.UtcNow };
            return View(info);
        }

        // POST: ShippingInfos/Create
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Content,ShippingId,Id,CreatedAt")] ShippingInfo shippingInfo)
        {
            if (ModelState.IsValid)
            {
                var shipping = await _context.Shippings.FindAsync(shippingInfo.ShippingId);
                //shippingInfo.Shipping = shipping;
                shipping.ShippingInfos = new List<ShippingInfo>();
                shipping.ShippingInfos.Add(new ShippingInfo { Content = shippingInfo.Content, CreatedAt = shippingInfo.CreatedAt });
                _context.Update(shipping);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index), new { id = shipping.Id });
            }
            //ViewData["ShippingId"] = new SelectList(_context.Shippings, "Id", "Id", shippingInfo.ShippingId);
            return View(shippingInfo);
        }

        // GET: ShippingInfos/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var shippingInfo = await _context.ShippingInfos.FindAsync(id);
            if (shippingInfo == null)
            {
                return NotFound();
            }
            //ViewData["ShippingId"] = new SelectList(_context.Shippings, "Id", "Id", shippingInfo.ShippingId);
            return View(shippingInfo);
        }

        // POST: ShippingInfos/Edit/5
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Content,ShippingId,Id,CreatedAt")] ShippingInfo shippingInfo)
        {
            if (id != shippingInfo.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(shippingInfo);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ShippingInfoExists(shippingInfo.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index), new { id = shippingInfo.ShippingId });
            }
            ViewData["ShippingId"] = new SelectList(_context.Shippings, "Id", "Id", shippingInfo.ShippingId);
            return View(shippingInfo);
        }

        // GET: ShippingInfos/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var shippingInfo = await _context.ShippingInfos
                .Include(s => s.Shipping)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (shippingInfo == null)
            {
                return NotFound();
            }

            return View(shippingInfo);
        }

        // POST: ShippingInfos/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var shippingInfo = await _context.ShippingInfos.FindAsync(id);
            _context.ShippingInfos.Remove(shippingInfo);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool ShippingInfoExists(int id)
        {
            return _context.ShippingInfos.Any(e => e.Id == id);
        }

        private void SetShippingId(int? id)
        {
            ViewData["CurrentShippingId"] = id;
        }
    }
}
