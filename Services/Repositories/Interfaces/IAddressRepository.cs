using Services.Repositories.Data.AddressData;

namespace Services.Repositories.Interfaces
{
    public interface IAddressRepository
    {
        Task<Address> CreateAddress(Address address);
        Task<Address> GetAddress(int addressId);
        Task<List<Address>> GetAddresses(List<int> addressIds);
        Task<Address> UpdateAddress(Address address);
        Task<List<Address>> SearchAddresses(Address address);
    }
}
