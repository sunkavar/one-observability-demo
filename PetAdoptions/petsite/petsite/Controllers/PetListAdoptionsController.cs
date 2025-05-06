using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PetSite.Models;
// Xray-To-Otel using Amazon.XRay.Recorder.Handlers.AwsSdk;
using System.Net.Http;
// Xray-To-Otel using Amazon.XRay.Recorder.Handlers.System.Net;
// Xray-To-Otel using Amazon.XRay.Recorder.Core;
using System.Text.Json;
using PetSite.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Trace; // Added OpenTelemetry

namespace PetSite.Controllers
{
    public class PetListAdoptionsController : Controller
    {
        private static HttpClient _httpClient;
        private IConfiguration _configuration;
        private readonly ActivitySource _activitySource;
        private readonly ILogger<PetListAdoptionsController> _logger;

        public PetListAdoptionsController(IConfiguration configuration, Instrumentation instrumentation, ILogger<PetListAdoptionsController> logger)
        {
            _configuration = configuration;
            _activitySource = instrumentation.ActivitySource;
            // Xray-To-Otel AWSSDKHandler.RegisterXRayForAllServices();

            // Xray-To-Otel _httpClient = new HttpClient(new HttpClientXRayTracingHandler(new HttpClientHandler()));
            // Initialize HttpClient once, relying on OTEL instrumentation from Program.cs
            if (_httpClient == null)
            {
                _httpClient = new HttpClient();
            }
            _logger = logger;
        }

        // GET
        public async Task<IActionResult> Index()
        {
            // Xray-To-Otel AWSXRayRecorder.Instance.BeginSubsegment("Calling PetListAdoptions");
            using (var activity = _activitySource.StartActivity("Calling PetListAdoptions"))
            {
                // Xray-To-Otel Console.WriteLine($"[{AWSXRayRecorder.Instance.GetEntity().TraceId}][{AWSXRayRecorder.Instance.TraceContext.GetEntity().RootSegment.TraceId}] - Calling PetListAdoptions API");
                // Console.WriteLine("Calling PetListAdoptions API");
                _logger.LogInformation("Calling PetListAdoptions API");

                string result;
                List<Pet> Pets = new List<Pet>();

                try
                {
                    //string petlistadoptionsurl = _configuration["petlistadoptionsurl"];
                    string petlistadoptionsurl = SystemsManagerConfigurationProviderWithReloadExtensions.GetConfiguration(_configuration, "petlistadoptionsurl");

                    activity?.SetTag("url", petlistadoptionsurl);
                    result = await _httpClient.GetStringAsync($"{petlistadoptionsurl}");
                    Pets = JsonSerializer.Deserialize<List<Pet>>(result);
                    _logger.LogInformation("Retrieved {PetCount} adoptions from PetListAdoptions API", Pets.Count);
                }
                catch (Exception e)
                {
                    // Xray-To-Otel AWSXRayRecorder.Instance.AddException(e);
                    activity?.AddException(e); // OTEL exception tracking
                    throw;
                }
                finally
                {
                    // Xray-To-Otel AWSXRayRecorder.Instance.EndSubsegment();
                }

                return View(Pets);
            }
        }
    }
}