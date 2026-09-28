using MarketLink.Models;

namespace MarketLink.ViewModels
{
    public class MarketsViewModel
    {
        public List<MarketCardViewModel> Markets { get; set; } = new();
    }

    public class MarketCardViewModel
    {
        public Market Market { get; set; } = new();

        public List<MarketDay> MarketDays { get; set; } = new();

        public int FarmerCount { get; set; }

        public string ImageUrl { get; set; } = "";

        public string FirstDayName
        {
            get
            {
                return MarketDays.FirstOrDefault()?.DayName ?? "Not Available";
            }
        }

        public string FirstDayLower
        {
            get
            {
                return FirstDayName.ToLower().Trim();
            }
        }

        public string AllDays
        {
            get
            {
                return string.Join(
                    "|",
                    MarketDays.Select(x => x.DayName.ToLower().Trim())
                );
            }
        }

        public string OperatingTime
        {
            get
            {
                var day = MarketDays.FirstOrDefault();

                if (day == null)
                    return "Time not available";

                return $"{FormatTime(day.OpeningTime)} – {FormatTime(day.ClosingTime)}";
            }
        }

        public string DisplayDays
        {
            get
            {
                if (!MarketDays.Any())
                    return "Schedule not available";

                return string.Join(
                    ", ",
                    MarketDays.Select(x => x.DayName)
                );
            }
        }

        private static string FormatTime(TimeSpan time)
        {
            return DateTime.Today
                .Add(time)
                .ToString("h tt");
        }
    }
}