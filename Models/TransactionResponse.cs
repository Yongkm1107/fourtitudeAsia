namespace fourtitudeAsia.Models
{
    public class TransactionResponse
    {
        public int Result { get; set; } // 1 = success, 0 = failure
        public long? TotalAmount { get; set; }
        public long? TotalDiscount { get; set; }
        public long? FinalAmount { get; set; }
        public string? ResultMessage { get; set; }
    }
}
