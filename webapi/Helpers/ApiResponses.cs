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

        // 400 that also carries data: for a rejection the client can recover from on its own,
        // such as "the figures moved; here are the fresh ones". Same envelope, Data filled in.
        public IActionResult BadRequestResult(string error, object data)
        {
            APIResponse response = new APIResponse();
            response.Status = BadRequest().StatusCode.ToString();
            response.Message = APIResponseMessage.Fail;
            response.Error = error ?? "";
            response.Data = data;
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

        // 409 that also carries data, for a refusal the client acts on rather than just shows: which
        // case it is (a reason code) and anything needed to recover, such as the checkout to resume.
        public IActionResult ConflictResult(string error, object data)
        {
            APIResponse response = new APIResponse();
            response.Status = Conflict().StatusCode.ToString();
            response.Message = APIResponseMessage.Fail;
            response.Error = error ?? "";
            response.Data = data;
            return Conflict(response);
        }

        // 502: an upstream we depend on (the payment processor) failed, and nothing was done on our
        // side either. Same envelope, so clients read .error.
        public IActionResult BadGatewayResult(object obj = null)
        {
            APIResponse response = new APIResponse();
            response.Status = "502";
            response.Message = APIResponseMessage.Fail;
            response.Error = obj != null ? obj.ToString() : "";
            return StatusCode(502, response);
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
