using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using webapi.Models;
using Services.Helpers;
using Services.Repositories.Interfaces;
using Services.Repositories.Data.ProductData;

namespace webapi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ProductController : ControllerBase
    {
        private readonly IProductRepository _productRepository;

        public ProductController(IProductRepository productRepository)
        {
            _productRepository = productRepository;
        }

        [HttpGet("GetProducts")]
        public async Task<IActionResult> GetProducts()
        {
            try
            {
                var products = await _productRepository.GetProducts();

                return new ApiResponses().OkResult(products);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [HttpGet("GetProduct")]
        public async Task<IActionResult> GetProduct([FromQuery] int id)
        {
            try
            {
                var product = await _productRepository.GetProduct(id);

                return new ApiResponses().OkResult(product);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpGet("GetProductOffers")]
        public async Task<IActionResult> GetProductOffers()
        {
            try
            {
                var offers = await _productRepository.GetAllProductOffers();

                return new ApiResponses().OkResult(offers);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("CreateProductOffer")]
        public async Task<IActionResult> CreateProductOffer([FromBody] ProductOfferRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var offer = new ProductOffer
                {
                    ShortDescription = request.ShortDescription,
                    LongDescription = request.LongDescription,
                    ProductId = request.ProductId,
                    OfferProductId = request.OfferProductId,
                    IsActive = request.IsActive
                };

                var result = await _productRepository.CreateProductOffer(offer);

                return new ApiResponses().OkResult(result);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("UpdateProductOffer")]
        public async Task<IActionResult> UpdateProductOffer([FromBody] ProductOfferRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var offer = new ProductOffer
                {
                    Id = request.Id ?? 0,
                    ShortDescription = request.ShortDescription,
                    LongDescription = request.LongDescription,
                    ProductId = request.ProductId,
                    OfferProductId = request.OfferProductId,
                    IsActive = request.IsActive
                };

                await _productRepository.UpdateProductOffer(offer);

                return new ApiResponses().OkResult(offer);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }
    }
}
