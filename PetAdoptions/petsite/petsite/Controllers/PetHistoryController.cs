using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace PetSite.Controllers;

public class PetHistoryController : Controller
{
    private IConfiguration _configuration;
    private readonly ILogger<PetHistoryController> _logger;
    private static HttpClient _httpClient;
    private static string _pethistoryurl;

    public PetHistoryController(IConfiguration configuration, ILogger<PetHistoryController> logger)
    {
        _configuration = configuration;
        
        if (_httpClient == null)
        {
            _httpClient = new HttpClient();
        }

        _pethistoryurl = _configuration["pethistoryurl"];
        //string _pethistoryurl = SystemsManagerConfigurationProviderWithReloadExtensions.GetConfiguration(_configuration,"pethistoryurl");
        _logger = logger;
    }

    /// <summary>
    /// GET:/pethistory
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        // Add custom attributes to the current activity
        Activity currentActivity = Activity.Current;
        if (currentActivity != null)
        {
            currentActivity.SetTag("operation", "get_pet_history");
            currentActivity.SetTag("service", "pet_history");
        }

        string url = $"{_pethistoryurl}/api/home/transactions";
        
        _logger.LogInformation("Fetching pet adoption history from {Url}", url);
        
        if (currentActivity != null)
        {
            currentActivity.SetTag("url", url);
        }
        
        ViewData["pethistory"] = await _httpClient.GetStringAsync($"{_pethistoryurl}/api/home/transactions");
        _logger.LogInformation("Pet adoption history retrieved successfully");
        
        return View();
    }

    /// <summary>
    /// DELETE:/deletepetadoptionshistory
    /// </summary>
    /// <returns></returns>
    [HttpDelete]
    public async Task<IActionResult> DeletePetAdoptionsHistory()
    {
        // Add custom attributes to the current activity
        Activity currentActivity = Activity.Current;
        if (currentActivity != null)
        {
            currentActivity.SetTag("operation", "delete_pet_history");
            currentActivity.SetTag("service", "pet_history");
        }

        string url = $"{_pethistoryurl}/api/home/transactions";
        
        _logger.LogInformation("Deleting pet adoption history at {Url}", url);
        
        if (currentActivity != null)
        {
            currentActivity.SetTag("url", url);
        }
        
        ViewData["pethistory"] = await _httpClient.DeleteAsync(url);
        _logger.LogInformation("Pet adoption history deleted successfully");
        
        return View("Index");
    }
}
