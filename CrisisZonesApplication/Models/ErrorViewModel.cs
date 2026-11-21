namespace CrisisZonesApplication.Models
{
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }

        public int? StatusCode { get; set; }

        public bool IsNotFound => StatusCode == 404;

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}
