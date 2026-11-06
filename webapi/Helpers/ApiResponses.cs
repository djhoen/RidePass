using Microsoft.AspNetCore.Mvc;

namespace Services.Helpers
{
    public class ApiResponses : Controller
    {
        public IActionResult OkResult(object obj = null)
        {
            APIResponse response = new APIResponse();
            response.Status = Ok().StatusCode.ToString();
            response.Data = obj;
            response.Message = APIResponseMessage.Success;
            return Ok(response);
        }

        public IActionResult BadRequestResult(object obj = null)
        {
            APIResponse response = new APIResponse();
            response.Status = BadRequest().StatusCode.ToString();
            response.Message = APIResponseMessage.Fail;
            response.Error = obj != null ? obj.ToString() : "";
            return BadRequest(response);
        }

        public IActionResult NotFoundResult(object obj = null)
        {
            APIResponse response = new APIResponse();
            response.Status = NotFound().StatusCode.ToString();
            response.Message = APIResponseMessage.Fail;
            response.Error = obj != null ? obj.ToString() : "";
            return NotFound(response);
        }

        // 409: the request was well-formed but the row it targets changed underneath it (another
        // register closed the tab first). Same envelope as BadRequestResult so clients read .error.
        public IActionResult ConflictResult(object obj = null)
        {
            APIResponse response = new APIResponse();
            response.Status = Conflict().StatusCode.ToString();
            response.Message = APIResponseMessage.Fail;
            response.Error = obj != null ? obj.ToString() : "";
            return Conflict(response);
        }
    }

    public class APIResponse
    {
        public string Status { get; set; }
        public object Data { get; set; }
        public string Message { get; set; }
        public string Error { get; set; }
    }

    public static class APIResponseMessage
    {
        public static String Success { get { return "Success"; } }
        public static String Fail { get { return "Error"; } }
    }
}
