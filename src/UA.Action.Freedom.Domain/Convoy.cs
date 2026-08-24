namespace UA.Action.Freedom.Domain
{
    public class Convoy
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public Veichle[] Veichles { get; set; }
        public DateTime Start { get; set; }
        public DateTime ExpectedEnd { get; set; }
        public Route Route { get; set; }
    }
}