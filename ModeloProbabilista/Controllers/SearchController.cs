using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using ModeloProbabilista.Models;
using ModeloProbabilista.Util;

namespace ModeloProbabilista.Controllers;

public class Request
{
    public string query { get; set; }
    public List<string> documents { get; set; }
}

public class SearchController : Controller
{
    private static IndexGeneretor? _indexInstance;
    private readonly ILogger<SearchController> _logger;

    public SearchController(ILogger<SearchController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult StartPage()
    {
        return View();
    }

    [HttpGet("Authors")]
    public IActionResult Authors()
    {
        return View();
    }

    [HttpGet("SearchResult")]
    public async Task<IActionResult> SearchResult(string query)
    {
        if (_indexInstance is null)
            _indexInstance = new IndexGeneretor();

        var result = await IndexGeneretor.Search(query);

        return View(result);
    }
    
    [HttpPost("SearchResultFeedback")]
    public async Task<IActionResult> SearchResultFeedback([FromBody] Request req)
    {
        if (_indexInstance is null)
            _indexInstance = new IndexGeneretor();

        var result = await IndexGeneretor.RelevanceFeedbackSearch(req.query, req.documents);
        var ret = Json(result);

        return ret;
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    public IActionResult OpenHtml(string fileName)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = $"{Directory.GetParent(Environment.CurrentDirectory)?.FullName}\\ModeloProbabilista\\Data\\htmlFiles";
        var filePath = Path.Combine(path, fileName);

        var fileHtml = System.IO.File.ReadAllText(filePath, Encoding.GetEncoding(1252));
        return Content(fileHtml, "text/html");
    }

    public async Task<IActionResult> ResetIndex()
    {
        _indexInstance = new IndexGeneretor();
        var projectDirectory = Directory.GetParent(Environment.CurrentDirectory)?.FullName;
        var pathFolderIdf = projectDirectory + "\\ModeloProbabilista\\Data\\idf.json";
        var pathFolderTfidf = projectDirectory + "\\ModeloProbabilista\\Data\\termsInDocs.json";
        System.IO.File.Delete(pathFolderIdf);
        System.IO.File.Delete(pathFolderTfidf);
        await IndexGeneretor.StartIndex();
        return Ok();
    }
}