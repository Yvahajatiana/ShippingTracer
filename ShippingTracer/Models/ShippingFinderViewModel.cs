using ShippingTracer.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace ShippingTracer.Models
{
    public class ShippingFinderViewModel
    {
        [Required]
        [Display(Name = "Tracking Number")]
        public string UniqueId { get; set; }
    }
}
