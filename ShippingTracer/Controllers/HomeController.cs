using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShippingTracer.Data;
using ShippingTracer.Models;

namespace ShippingTracer.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            if (!_context.ShippingStatuses.Any())
            {
                _context.ShippingStatuses
                    .Add(new ShippingStatus { Name = "En Préparation", Description = "", CreatedAt = DateTime.UtcNow });
                _context.ShippingStatuses
                    .Add(new ShippingStatus { Name = "Envoyé chez le transporteur", Description = "", CreatedAt = DateTime.UtcNow });
                _context.ShippingStatuses
                    .Add(new ShippingStatus { Name = "Arrivé à Tana", Description = "", CreatedAt = DateTime.UtcNow });
                _context.ShippingStatuses
                    .Add(new ShippingStatus { Name = "Encours de livraison", Description = "", CreatedAt = DateTime.UtcNow });
                _context.ShippingStatuses
                    .Add(new ShippingStatus { Name = "Livré", Description = "", CreatedAt = DateTime.UtcNow });

                _context.SaveChanges();
            }

            if(TempData["NoResult"] != null)
            {
                ViewData["NoResult"] = TempData["NoResult"];
            }

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [HttpPost]
        public IActionResult FindShipping([Bind("UniqueId")] ShippingFinderViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var shipping = _context.Shippings
                .Include(s => s.ShippingStatus)
                .Include(s => s.ShippingInfos)
                .Include(s => s.Customer)
                .FirstOrDefault(x => x.UniqueId == model.UniqueId);
            if(shipping == null)
            {
                TempData["NoResult"] = model.UniqueId;
                return RedirectToAction("Index");
            }

            return View(shipping);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
