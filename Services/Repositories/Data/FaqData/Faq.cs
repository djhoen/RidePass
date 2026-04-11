namespace Services.Repositories.Data.FaqData
{
    public class Faq
    {
        public int Id { get; set; }
        public int FaqTypeId { get; set; }
        public string Question { get; set; }
        public string Answer { get; set; }
        public bool Expanded { get; set; }
    }

    public enum FaqType
    {
        General = 1,
        Checkout = 2
    }

    public class SaveFaqRequest
    {
        public int? Id { get; set; }
        public int FaqTypeId { get; set; }
        public string Question { get; set; }
        public string Answer { get; set; }
    }
}
