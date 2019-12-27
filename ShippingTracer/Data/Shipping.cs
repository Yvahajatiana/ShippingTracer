using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace ShippingTracer.Data
{
    public class Shipping : EntityBase
    {
        [Display(Name = "Tracking Number")]
        public string UniqueId { get; set; }

        [Display(Name = "Status")]
        public int ShippingStatusId { get; set; }

        [Display(Name = "Status")]
        public virtual ShippingStatus ShippingStatus { get; set; }

        //[DataType(DataType.DateTime)]
        [DisplayFormat(ApplyFormatInEditMode = true, DataFormatString = "{0:MM/dd/yyyy}")]
        [Display(Name = "Departure Date")]
        public DateTime DepartureDate { get; set; }

        //[DataType(DataType.DateTime)]
        [DisplayFormat(ApplyFormatInEditMode = true, DataFormatString = "{0:MM/dd/yyyy}")]
        [Display(Name = "Estimated Delivery Date")]
        public DateTime EstimatedDeliveryDate { get; set; }

        [Display(Name = "Customer")]
        public int CustomerId { get; set; }

        [Display(Name = "Customer")]
        public virtual Customer Customer { get; set; }

        public virtual ICollection<ShippingInfo> ShippingInfos { get; set; }
    }
}
