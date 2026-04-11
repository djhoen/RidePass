using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using webapi.Models;
using Services.Helpers;
using Services.Repositories.Interfaces;
using Services.Repositories.Data.AddressData;

namespace webapi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class AddressController : ControllerBase
    {
        private readonly IAddressRepository _addressRepository;

        public AddressController(IAddressRepository addressRepository)
        {
            _addressRepository = addressRepository;
        }

        [HttpGet("GetAddress")]
        public async Task<IActionResult> GetAddress([FromQuery] int id)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var address = await _addressRepository.GetAddress(id);

                return new ApiResponses().OkResult(address);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [HttpPost("CreateAddress")]
        public async Task<IActionResult> CreateAddress([FromBody] AddressRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var address = new Address
                {
                    Addr1 = request.Addr1,
                    Addr2 = request.Addr2,
                    City = request.City,
                    StateCode = request.StateCode,
                    Zip = request.Zip,
                    CountryCode = request.CountryCode,
                    Name = request.Name
                };

                var result = await _addressRepository.CreateAddress(address);

                return new ApiResponses().OkResult(result);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [HttpPost("UpdateAddress")]
        public async Task<IActionResult> UpdateAddress([FromBody] AddressRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var address = new Address
                {
                    Id = request.Id ?? 0,
                    Addr1 = request.Addr1,
                    Addr2 = request.Addr2,
                    City = request.City,
                    StateCode = request.StateCode,
                    Zip = request.Zip,
                    CountryCode = request.CountryCode,
                    Name = request.Name
                };

                var result = await _addressRepository.UpdateAddress(address);

                return new ApiResponses().OkResult(result);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }
    }
}
