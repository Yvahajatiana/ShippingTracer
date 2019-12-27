using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ShippingTracer.Data
{
    public class ShippingInfo : EntityBase
    {
        public string Content { get; set; }

        public int ShippingId { get; set; }

        public virtual Shipping Shipping { get; set; }
    }
}
