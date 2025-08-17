namespace fourtitudeAsia.Models
{
    public class TransactionRequest
    {
        public string PartnerKey { get; set; } = string.Empty;      // M
        public string PartnerRefNo { get; set; } = string.Empty;    // M
        public string PartnerPassword { get; set; } = string.Empty; // M, Base64
        public long TotalAmount { get; set; }                       // M, positive cents
        public List<ItemDetail>? Items { get; set; }                // O
        public string Timestamp { get; set; } = string.Empty;       // M, ISO 8601
        public string Sig { get; set; } = string.Empty;              // M
    }
}
