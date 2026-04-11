using Services.Repositories.Data.FaqData;

namespace Services.Repositories.Interfaces
{
    public interface IFaqRepository
    {
        Task<List<Faq>> GetFaqs();
        Task CreateFaq(Faq faq);
        Task DeleteFaq(int id);
        Task UpdateFaq(Faq faq);
    }
}
