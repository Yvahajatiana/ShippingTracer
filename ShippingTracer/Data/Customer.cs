using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace ShippingTracer.Data
{
    public class Customer : EntityBase
    {
        [Required(ErrorMessage = "The firstname field is mandatory")]
        [Display(Name = "Firstname")]
        public string FirstName { get; set; }

        [Display(Name = "Lastname")]
        public string LastName { get; set; }

        [Required(ErrorMessage = "The phone number field is mandatory")]
        [Display(Name = "Phone")]
        public string Phone { get; set; }

        [Required(ErrorMessage = "The address field is mandatory")]
        [Display(Name = "Address")]
        public string Address { get; set; }

        public virtual ICollection<Shipping> Shippings { get; set; }
    }
}
