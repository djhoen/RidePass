using Services.Helpers.Interfaces;
using Services.Repositories.Data.AddressData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class AddressRepository : IAddressRepository
    {
        private readonly IDbHelper _dbHelper;
        public AddressRepository(IDbHelper doDbHelper)
        {
            _dbHelper = doDbHelper;
        }

        public Address CleanAddress(Address address)
        {
            if (address == null)
            {
                address = new Address();
            }

            address.Addr1 = address.Addr1?.Trim();
            address.Addr2 = address.Addr2?.Trim();
            address.City = address.City?.Trim();
            address.StateCode = address.StateCode?.Trim();
            address.Zip = address.Zip?.Trim();
            address.CountryCode = address.CountryCode?.Trim();
            address.Name = address.Name?.Trim();

            return address;
        }

        public async Task<Address> CreateAddress(Address address)
        {
            address = CleanAddress(address);

            var sql = @"INSERT INTO ""address"" (""addr1"", ""addr2"", ""city"", ""stateCode"", ""zip"", ""countryCode"", ""name"")
                        VALUES (@addr1, @addr2, @city, @stateCode, @zip, @countryCode, @name)
                        ON CONFLICT (""id"") DO NOTHING
                        RETURNING ""id""";

            var id = await _dbHelper.Query<int>(sql, address);

            address.Id = id.FirstOrDefault();

            return address;
        }

        public async Task<Address> GetAddress(int addressId)
        {
            var sql = @"SELECT * FROM ""address"" WHERE ""id"" = @addressId";

            var addresses = await _dbHelper.Query<Address>(sql, new { addressId });

            return addresses.FirstOrDefault();
        }

        public async Task<List<Address>> GetAddresses(List<int> addressIds)
        {
            var sql = @"SELECT * FROM ""address"" WHERE ""id"" = ANY (@addressIds)";

            var addresses = await _dbHelper.Query<Address>(sql, new { addressIds });

            return addresses.ToList();
        }

        public async Task<List<Address>> SearchAddresses(Address address)
        {
            address = CleanAddress(address);

            var sql = @"SELECT * FROM ""address""
                        WHERE LOWER(addr1) = LOWER(@addr1)
                            AND LOWER(name) = LOWER(@name)
                            AND LOWER(city) = LOWER(@city)";

            var addresses = await _dbHelper.Query<Address>(sql, address);

            return addresses.ToList();
        }

        public async Task<Address> UpdateAddress(Address address)
        {
            var sql = @"UPDATE ""address""
                        SET ""addr1"" = @addr1,
                            ""addr2"" = @addr2,
                            ""city"" = @city,
                            ""stateCode"" = @stateCode,
                            ""zip"" = @zip,
                            ""countryCode"" = @countryCode,
                            ""name"" = @name
                        WHERE ""id"" = @id";

            await _dbHelper.Execute(sql, address);

            return address;
        }
    }
}
