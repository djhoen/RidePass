using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using webapi.Models;
using Services.Helpers;
using Services.Repositories.Interfaces;
using Services.Repositories.Data.SiteData;

namespace webapi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SiteController : ControllerBase
    {
        private readonly ISiteRepository _siteRepository;

        public SiteController(ISiteRepository siteRepository)
        {
            _siteRepository = siteRepository;
        }

        [HttpGet("GetBanner")]
        public async Task<IActionResult> GetBanner()
        {
            try
            {
                var banner = await _siteRepository.GetBanner();

                return new ApiResponses().OkResult(banner);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpGet("GetBanners")]
        public async Task<IActionResult> GetBanners()
        {
            try
            {
                var banners = await _siteRepository.GetBanners();

                return new ApiResponses().OkResult(banners);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("CreateBanner")]
        public async Task<IActionResult> CreateBanner([FromBody] BannerRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var banner = new Banner
                {
                    Name = request.Name,
                    Text = request.Text,
                    ActionUrl = request.ActionUrl,
                    IsActive = request.IsActive,
                    Class = request.Class
                };

                var result = await _siteRepository.CreateBanner(banner);

                return new ApiResponses().OkResult(result);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("UpdateBanner")]
        public async Task<IActionResult> UpdateBanner([FromBody] BannerRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var banner = new Banner
                {
                    Id = request.Id ?? 0,
                    Name = request.Name,
                    Text = request.Text,
                    ActionUrl = request.ActionUrl,
                    IsActive = request.IsActive,
                    Class = request.Class
                };

                await _siteRepository.UpdateBanner(banner);

                return new ApiResponses().OkResult(banner);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpGet("GetSetting")]
        public async Task<IActionResult> GetSetting([FromQuery] string key)
        {
            try
            {
                var setting = await _siteRepository.GetSettingByName(key);

                return new ApiResponses().OkResult(setting);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("SaveSetting")]
        public async Task<IActionResult> SaveSetting([FromBody] SettingRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var setting = new Setting
                {
                    Id = request.Id ?? 0,
                    Name = request.Name,
                    Value = request.Value,
                    Category = request.Category
                };

                var result = await _siteRepository.SaveSetting(setting);

                return new ApiResponses().OkResult(result);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }
    }
}
