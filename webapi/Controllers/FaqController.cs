using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using webapi.Models;
using Services.Helpers;
using Services.Repositories.Interfaces;
using Services.Repositories.Data.FaqData;

namespace webapi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class FaqController : ControllerBase
    {
        private readonly IFaqRepository _faqRepository;

        public FaqController(IFaqRepository faqRepository)
        {
            _faqRepository = faqRepository;
        }

        [HttpGet("GetFaqs")]
        public async Task<IActionResult> GetFaqs()
        {
            try
            {
                var faqs = await _faqRepository.GetFaqs();

                return new ApiResponses().OkResult(faqs);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("CreateFaq")]
        public async Task<IActionResult> CreateFaq([FromBody] FaqRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var faq = new Faq
                {
                    FaqTypeId = request.FaqTypeId,
                    Question = request.Question,
                    Answer = request.Answer
                };

                await _faqRepository.CreateFaq(faq);

                return new ApiResponses().OkResult(faq);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("UpdateFaq")]
        public async Task<IActionResult> UpdateFaq([FromBody] FaqRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var faq = new Faq
                {
                    Id = request.Id ?? 0,
                    FaqTypeId = request.FaqTypeId,
                    Question = request.Question,
                    Answer = request.Answer
                };

                await _faqRepository.UpdateFaq(faq);

                return new ApiResponses().OkResult(faq);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("DeleteFaq")]
        public async Task<IActionResult> DeleteFaq([FromBody] DeleteRequest request)
        {
            try
            {
                await _faqRepository.DeleteFaq(request.Id);

                return new ApiResponses().OkResult(null);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }
    }
}
