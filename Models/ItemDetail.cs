namespace fourtitudeAsia.Models
{
    public class ItemDetail
    {
        public string PartnerItemRef { get; set; } = string.Empty; // M, not null/empty
        public string Name { get; set; } = string.Empty;           // M, not null/empty
        public int Qty { get; set; }                               // M, positive, <= 5
        public long UnitPrice { get; set; }                        // M, positive cents value
    }
}