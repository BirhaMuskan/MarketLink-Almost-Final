using System.ComponentModel.DataAnnotations;

namespace MarketLink.ViewModels
{
    public class ContactPageViewModel
    {
        [Required, StringLength(100)]
        [Display(Name = "Your Name")]
        public string Name { get; set; } = "";

        [Required, EmailAddress, StringLength(150)]
        [Display(Name = "Email Address")]
        public string Email { get; set; } = "";

        [StringLength(30)]
        [Display(Name = "Phone Number")]
        public string? Phone { get; set; }

        [Required, StringLength(50)]
        [Display(Name = "Inquiry Type")]
        public string InquiryType { get; set; } = "General";

        [Required, StringLength(150)]
        public string Subject { get; set; } = "";

        [Required, StringLength(4000, MinimumLength = 10)]
        [Display(Name = "Your Message")]
        public string Message { get; set; } = "";

        public int? TargetFarmerId { get; set; }
        public string? TargetFarmerName { get; set; }

        // Dynamic public contact information
        public string Location { get; set; } = "Karachi, Sindh, Pakistan";
        public string SupportEmail { get; set; } = "hello@marketlink.com";
        public string PhoneNumber { get; set; } = "+92 300 1234567";
        public string SupportHours { get; set; } = "Mon - Sat, 09:00 AM - 06:00 PM";

        public string? FacebookUrl { get; set; }
        public string? InstagramUrl { get; set; }
        public string? TwitterUrl { get; set; }
        public string? LinkedInUrl { get; set; }

        public int ActiveMarketCount { get; set; }
        public int AvailableProductCount { get; set; }
        public int ActiveFarmerCount { get; set; }

        public string? SuccessMessage { get; set; }
        public string? ReferenceNumber { get; set; }
    }
}
