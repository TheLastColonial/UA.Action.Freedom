namespace UA.Action.Freedom.Domain
{
    /// <summary>
    /// Truck or car that is being donated
    /// </summary>
    public class Veichle
    {
        public string VIN { get; set; }
        public string Plate { get; set; }
        public string? Description { get; set; }
        public string? Brand { get; set; }
        public string? Model { get; set; }
        public string? Notes { get; set; }
        public int? Miles { get; set; }
        public Convoy Convoy { get; set; }
        public string Purchaser { get; set; }
        public DateTime PurchaseDate { get; set; }
        public string[]? Drivers { get; set; }
        public int WeightKg { get; internal set; }
    }
}
