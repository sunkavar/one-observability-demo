using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
// Xray-To-Otel using Amazon.XRay.Recorder.Core;
// Xray-To-Otel using Amazon.XRay.Recorder.Handlers.AwsSdk;
// Xray-To-Otel using Amazon.XRay.Recorder.Handlers.System.Net;
using OpenTelemetry.Trace; // Added OpenTelemetry
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using PetSite.ViewModels;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PetSite.Controllers
{
    public class AdoptionController : Controller
    {
        private static readonly HttpClient HttpClient = new HttpClient(); // Removed X-Ray handler, relies on OTEL
        private static Variety _variety = new Variety();
        private static IConfiguration _configuration;
        private static string _searchApiurl;
        private readonly ActivitySource _activitySource;
        private readonly ILogger<AdoptionController> _logger;

        public AdoptionController(IConfiguration configuration, Instrumentation instrumentation, ILogger<AdoptionController> logger)
        {
            _configuration = configuration;
            //_searchApiurl = _configuration["searchapiurl"];
            _searchApiurl = SystemsManagerConfigurationProviderWithReloadExtensions.GetConfiguration(_configuration, "searchapiurl");
            _activitySource = instrumentation.ActivitySource;
            // Xray-To-Otel AWSSDKHandler.RegisterXRayForAllServices();
            _logger = logger;
        }

        // GET: Adoption
        [HttpGet]
        public IActionResult Index([FromQuery] Pet pet)
        {
            return View(pet);
        }

        private async Task<string> GetPetDetails(SearchParams searchParams)
        {
            string searchString = string.Empty;

            if (!String.IsNullOrEmpty(searchParams.pettype) && searchParams.pettype != "all") searchString = $"pettype={searchParams.pettype}";
            if (!String.IsNullOrEmpty(searchParams.petcolor) && searchParams.petcolor != "all") searchString = $"&{searchString}&petcolor={searchParams.petcolor}";
            if (!String.IsNullOrEmpty(searchParams.petid) && searchParams.petid != "all") searchString = $"&{searchString}&petid={searchParams.petid}";

            return await HttpClient.GetStringAsync($"{_searchApiurl}{searchString}");
        }

        [HttpPost]
        public async Task<IActionResult> TakeMeHome([FromForm] SearchParams searchParams)
        {
            // Xray-To-Otel Console.WriteLine(
            //    $"[{AWSXRayRecorder.Instance.TraceContext.GetEntity().RootSegment.TraceId}][{AWSXRayRecorder.Instance.GetEntity().TraceId}] - Inside TakeMehome. Pet in context - PetId:{searchParams.petid}, PetType:{searchParams.pettype}, PetColor:{searchParams.petcolor}");
            using (var activity = _activitySource.StartActivity("TakeMeHome"))
            {
                //Console.WriteLine($"Inside TakeMehome. Pet in context - PetId:{searchParams.petid}, PetType:{searchParams.pettype}, PetColor:{searchParams.petcolor}");
                _logger.LogInformation("Inside TakeMeHome - PetId:{PetId}, PetType:{PetType}, PetColor:{PetColor}",
                    searchParams.petid, searchParams.pettype, searchParams.petcolor);

                // Xray-To-Otel AWSXRayRecorder.Instance.AddMetadata("PetType", searchParams.pettype);
                // Xray-To-Otel AWSXRayRecorder.Instance.AddMetadata("PetId", searchParams.petid);
                // Xray-To-Otel AWSXRayRecorder.Instance.AddMetadata("PetColor", searchParams.petcolor);
                activity?.SetTag("pet.type", searchParams.pettype);
                activity?.SetTag("pet.id", searchParams.petid);
                activity?.SetTag("pet.color", searchParams.petcolor);

                // Xray-To-Otel String traceId = TraceId.NewId(); // This function is present in : Amazon.XRay.Recorder.Core.Internal.Entities
                // Xray-To-Otel AWSXRayRecorder.Instance.BeginSubsegment("Calling Search API"); // custom traceId used while creating segment
                string result;

                try
                {
                    using (var subActivity = _activitySource.StartActivity("Calling Search API"))
                    {
                        subActivity?.SetTag("url", $"{_searchApiurl}");
                        result = await GetPetDetails(searchParams);
                        _logger.LogInformation("Successfully called search API for PetId:{PetId}", searchParams.petid);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to call search API for PetId:{PetId}, PetType:{PetType}",
                        searchParams.petid, searchParams.pettype);
                    // Xray-To-Otel AWSXRayRecorder.Instance.AddException(e);
                    activity?.AddException(e); // OTEL exception tracking
                    throw;
                }
                finally
                {
                    // Xray-To-Otel AWSXRayRecorder.Instance.EndSubsegment();
                }

                return View("Index", JsonSerializer.Deserialize<List<Pet>>(result).FirstOrDefault());
            }
        }
    }
}