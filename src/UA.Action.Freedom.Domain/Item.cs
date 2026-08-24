namespace UA.Action.Freedom.Domain
{
    public class Item
    {
        public Guid Id { get; set; }
        public string Description { get; set; }
        public Dictionary<string,string> Properties { get; set; }
    }
}