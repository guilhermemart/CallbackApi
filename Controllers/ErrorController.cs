using Microsoft.AspNetCore.Mvc;

namespace CallbackApi.Controllers;

[ApiController]
public class ErrorController : ControllerBase
{
    [Route("/error")]
    [HttpGet]
    [HttpPost]
    [HttpPut]
    [HttpDelete]
    [HttpPatch]
    public IActionResult HandleError()
    {
        return Problem(
            title: "An unexpected error occurred",
            statusCode: 500
        );
    }
}
