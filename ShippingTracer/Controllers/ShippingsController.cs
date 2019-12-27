using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ShippingTracer.Data;

namespace ShippingTracer.Controllers
{
    [Authorize]
    public class ShippingsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ShippingsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Shippings
        public async Task<IActionResult> Index()
        {
            var applicationDbContext = _context.Shippings.Include(s => s.ShippingStatus).Include(s => s.Customer);
            return View(await applicationDbContext.ToListAsync());
        }

        // GET: Shippings/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var shipping = await _context.Shippings
                .Include(s => s.ShippingStatus)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (shipping == null)
            {
                return NotFound();
            }

            return View(shipping);
        }

        // GET: Shippings/Create
        public IActionResult Create()
        {
            ViewData["ShippingStatusId"] = new SelectList(_context.ShippingStatuses, "Id", "Name");
            ViewData["CustomerId"] = new SelectList(_context.Customers, "Id", "FirstName");
            return View();
        }

        // POST: Shippings/Create
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("CustomerId, ShippingStatusId,DepartureDate,EstimatedDeliveryDate,Id")] Shipping shipping)
        {
            if (ModelState.IsValid)
            {
                shipping.CreatedAt = DateTime.UtcNow;
                var guid = Guid.NewGuid().ToString();
                shipping.UniqueId = guid.Substring(19, 17).ToUpper();

                _context.Add(shipping);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["ShippingStatusId"] = new SelectList(_context.ShippingStatuses, "Id", "Name", shipping.ShippingStatusId);
            ViewData["CustomerId"] = new SelectList(_context.Customers, "Id", "FirstName", shipping.CustomerId);
            return View(shipping);
        }

        // GET: Shippings/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var shipping = await _context.Shippings.FindAsync(id);
            if (shipping == null)
            {
                return NotFound();
            }
            ViewData["ShippingStatusId"] = new SelectList(_context.ShippingStatuses, "Id", "Name", shipping.ShippingStatusId);
            ViewData["CustomerId"] = new SelectList(_context.Customers, "Id", "FirstName", shipping.CustomerId);
            return View(shipping);
        }

        // POST: Shippings/Edit/5
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("UniqueId, CreatedAt ,ShippingStatusId,CustomerId, DepartureDate,EstimatedDeliveryDate,Id")] Shipping shipping)
        {
            if (id != shipping.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(shipping);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ShippingExists(shipping.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["ShippingStatusId"] = new SelectList(_context.ShippingStatuses, "Id", "Name", shipping.ShippingStatusId);
            ViewData["CustomerId"] = new SelectList(_context.Customers, "Id", "FirstName", shipping.CustomerId);
            return View(shipping);
        }

        // GET: Shippings/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var shipping = await _context.Shippings
                .Include(s => s.ShippingStatus)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (shipping == null)
            {
                return NotFound();
            }

            return View(shipping);
        }

        // POST: Shippings/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var shipping = await _context.Shippings.FindAsync(id);
            _context.Shippings.Remove(shipping);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool ShippingExists(int id)
        {
            return _context.Shippings.Any(e => e.Id == id);
        }
    }
}
