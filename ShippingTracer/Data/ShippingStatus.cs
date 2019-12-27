using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace ShippingTracer.Data
{
    public class ShippingStatus : EntityBase
    {
        [Required]
        public string Name { get; set; }

        public string Description { get; set; }

        public virtual ICollection<Shipping> Shippings { get; set; }
    }
}
