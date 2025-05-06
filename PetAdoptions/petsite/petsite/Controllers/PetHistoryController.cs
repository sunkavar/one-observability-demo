using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Net.Http;
using System.Threading.Tasks;
// Xray-To-Otel using Amazon.XRay.Recorder.Core;
using OpenTelemetry.Trace; // Added OpenTelemetry
// Xray-To-Otel using Amazon.XRay.Recorder.Handlers.System.Net;
// Xray-To-Otel using Amazon.XRay.Recorder.Handlers.AwsSdk;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Amazon.Runtime.Internal.Endpoints.StandardLibrary;

namespace PetSite.Controllers;

public class PetHistoryController : Controller
{
    private IConfiguration _configuration;
    private readonly ILogger<PetHistoryController> _logger;
    private static HttpClient _httpClient;
    private static string _pethistoryurl;
    private readonly ActivitySource _activitySource;

    public PetHistoryController(IConfiguration configuration, Instrumentation instrumentation, ILogger<PetHistoryController> logger)
    {
        _configuration = configuration;
        _activitySource = instrumentation.ActivitySource;
        // Xray-To-Otel AWSSDKHandler.RegisterXRayForAllServices();
        // Xray-To-Otel _httpClient = new HttpClient(new HttpClientXRayTracingHandler(new HttpClientHandler()));
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
        // Xray-To-Otel AWSXRayRecorder.Instance.BeginSubsegment("Calling GetPetAdoptionsHistory");
        using (var activity = _activitySource.StartActivity("Calling GetPetAdoptionsHistory"))
        {
            string url = $"{_pethistoryurl}/api/home/transactions";
            activity?.SetTag("url", url);
            _logger.LogInformation("Fetching pet adoption history from {Url}", url);
            ViewData["pethistory"] = await _httpClient.GetStringAsync($"{_pethistoryurl}/api/home/transactions");
            // Xray-To-Otel AWSXRayRecorder.Instance.EndSubsegment();
            _logger.LogInformation("Pet adoption history retrieved successfully");
        }
        return View();
    }

    /// <summary>
    /// DELETE:/deletepetadoptionshistory
    /// </summary>
    /// <returns></returns>
    [HttpDelete]
    public async Task<IActionResult> DeletePetAdoptionsHistory()
    {
        // Xray-To-Otel AWSXRayRecorder.Instance.BeginSubsegment("Calling DeletePetAdoptionsHistory");
        using (var activity = _activitySource.StartActivity("Calling DeletePetAdoptionsHistory"))
        {
            string url = $"{_pethistoryurl}/api/home/transactions";
            activity?.SetTag("url", url);
            _logger.LogInformation("Deleting pet adoption history at {Url}", url);
            ViewData["pethistory"] = await _httpClient.DeleteAsync(url);
            _logger.LogInformation("Pet adoption history deleted successfully");
            // Xray-To-Otel AWSXRayRecorder.Instance.EndSubsegment();
        }
        return View("Index");
    }
}