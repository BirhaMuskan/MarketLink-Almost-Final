namespace MarketLink.Services
{
    public sealed class EmailSettings
    {
        public bool Enabled { get; set; }
        public string Host { get; set; } = "smtp.gmail.com";
        public int Port { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string SenderName { get; set; } = "MarketLink";
        public string SenderEmail { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string AppBaseUrl { get; set; } = "";
    }
}
