using System.Collections.Generic;
using System.Linq;

namespace MarketLink.ViewModels
{
    public class WebsiteCartPageViewModel
    {
        public List<CustomerCartPageViewModel> Groups { get; set; } = new();

        public int ItemCount =>
            Groups.Sum(g => g.Items.Count);

        public decimal EstimatedTotal =>
            Groups.Sum(g => g.EstimatedTotal);
    }
}
