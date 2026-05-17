using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QRCoder;

namespace webapi.Controllers
{
    /// <summary>
    /// Renders a QR PNG for a purchase redemption token. Public so emails sent to riders
    /// can embed the image without auth (the token itself is the secret).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous]
    public class QrController : ControllerBase
    {
        [HttpGet("{token:guid}")]
        [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
        public IActionResult Get(Guid token)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(token.ToString(), QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data);
            // 10 pixels per module → ~290px image at typical QR sizes; renders cleanly in emails.
            var bytes = png.GetGraphic(10);
            return File(bytes, "image/png");
        }
    }
}
