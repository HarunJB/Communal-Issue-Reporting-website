using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace product_website.Controllers;

/// <summary>
/// Friendly error page for the live site (Program.cs sends unexpected errors here).
/// It is plain HTML on purpose, so it needs no view file and cannot fail itself.
/// </summary>
[AllowAnonymous]
public class ErrorController : Controller
{
    [Route("/Error")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult Index()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;

        const string html = """
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Something went wrong</title>
            <style>
              body{margin:0;font-family:system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;background:#F7F8F6;color:#17324D;
                   display:flex;min-height:100vh;align-items:center;justify-content:center}
              .card{background:#fff;border-top:4px solid #F2B544;border-radius:8px;padding:2rem 2.25rem;max-width:26rem;
                    box-shadow:0 2px 12px rgba(23,50,77,.08);text-align:center}
              h1{font-size:1.25rem;margin:0 0 .5rem} p{color:#475569;margin:0 0 1.25rem}
              a{display:inline-block;background:#2563A6;color:#fff;text-decoration:none;padding:.55rem 1.1rem;border-radius:6px}
            </style></head>
            <body><div class="card">
              <h1>Something went wrong</h1>
              <p>The page could not be shown. Please try again in a moment.</p>
              <a href="/Account/Login">Back to the start</a>
            </div></body></html>
            """;

        return Content(html, "text/html");
    }
}
