using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ShippingTracer.Data;

namespace ShippingTracer.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Shipping> Shippings { get; set; }

        public DbSet<ShippingInfo> ShippingInfos { get; set; }

        public DbSet<ShippingStatus> ShippingStatuses { get; set; }

        public DbSet<Customer> Customers { get; set; }
    }
}
