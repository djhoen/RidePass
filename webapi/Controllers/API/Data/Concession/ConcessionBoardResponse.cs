namespace webapi.Controllers.API.Data.Concession
{
    // Pickup number board for an in-venue display: today's live order numbers grouped into ready for
    // pickup vs still preparing. No line detail (it's a customer-facing screen).
    public class ConcessionBoardResponse
    {
        public List<Entry> Ready { get; set; } = new();
        public List<Entry> Preparing { get; set; } = new();

        public class Entry
        {
            public int? OrderNumber { get; set; }
            public string? CustomerName { get; set; }
        }
    }
}
