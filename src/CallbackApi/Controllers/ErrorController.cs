// Em Cs, muito provavelmente cada view precisa de um controller
// e cada controller tem uma classe que herda de ControllerBase ou Controller.
// O código fornecido é um exemplo de um controller de erro em uma aplicação ASP.NET Core.
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
