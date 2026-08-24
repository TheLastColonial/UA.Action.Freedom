namespace UA.Action.Freedom.Domain
{
    internal class Driver : Volunteer
    {
        public Convoy Convoy { get; set; }
        public bool Committed { get; set; }
    }
}
