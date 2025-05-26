using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PetSite.Models;
// Xray-To-Otel using Amazon.XRay.Recorder.Handlers.AwsSdk;
using System.Net.Http;
// Xray-To-Otel using Amazon.XRay.Recorder.Handlers.System.Net;
// Xray-To-Otel using Amazon.XRay.Recorder.Core;
using System.Text.Json;
using Amazon;
using PetSite.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Configuration;
using Prometheus;
using static PetSite.Startup;

namespace PetSite.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private static HttpClient _httpClient;
        private static Variety _variety = new Variety();

        private IConfiguration _configuration;

        //Prometheus metric to count the number of searches performed
        private static readonly Counter PetSearchCount =
            Metrics.CreateCounter("petsite_petsearches_total", "Count the number of searches performed");

        //Prometheus metric to count the number of puppy searches performed
        private static readonly Counter PuppySearchCount =
            Metrics.CreateCounter("petsite_pet_puppy_searches_total", "Count the number of puppy searches performed");

        //Prometheus metric to count the number of kitten searches performed
        private static readonly Counter KittenSearchCount =
            Metrics.CreateCounter("petsite_pet_kitten_searches_total", "Count the number of kitten searches performed");

        //Prometheus metric to count the number of bunny searches performed
        private static readonly Counter BunnySearchCount =
            Metrics.CreateCounter("petsite_pet_bunny_searches_total", "Count the number of bunny searches performed");

        private static readonly Gauge PetsWaitingForAdoption = Metrics
            .CreateGauge("petsite_pets_waiting_for_adoption", "Number of pets waiting for adoption.");

        public HomeController(ILogger<HomeController> logger, IConfiguration configuration)
        {
            _configuration = configuration;
            // HttpClient will use auto-instrumentation from the OpenTelemetry operator
            _httpClient = new HttpClient();
            _logger = logger;

            _variety.PetTypes = new List<SelectListItem>()
            {
                new SelectListItem() {Value = "all", Text = "All"},
                new SelectListItem() {Value = "puppy", Text = "Puppy"},
                new SelectListItem() {Value = "kitten", Text = "Kitten"},
                new SelectListItem() {Value = "bunny", Text = "Bunny"}
            };

            _variety.PetColors = new List<SelectListItem>()
            {
                new SelectListItem() {Value = "all", Text = "All"},
                new SelectListItem() {Value = "brown", Text = "Brown"},
                new SelectListItem() {Value = "black", Text = "Black"},
                new SelectListItem() {Value = "white", Text = "White"}
            };
        }

        private async Task<string> GetPetDetails(string pettype, string petcolor, string petid)
        {
            string searchUri = string.Empty;

            if (!String.IsNullOrEmpty(pettype) && pettype != "all") searchUri = $"pettype={pettype}";
            if (!String.IsNullOrEmpty(petcolor) && petcolor != "all") searchUri = $"&{searchUri}&petcolor={petcolor}";
            if (!String.IsNullOrEmpty(petid) && petid != "all") searchUri = $"&{searchUri}&petid={petid}";

            switch (pettype)
            {
                case "puppy":
                    PuppySearchCount.Inc();
                    PetSearchCount.Inc();
                    break;
                case "kitten":
                    KittenSearchCount.Inc();
                    PetSearchCount.Inc();
                    break;
                case "bunny":
                    BunnySearchCount.Inc();
                    PetSearchCount.Inc();
                    break;
            }
            //string searchapiurl = _configuration["searchapiurl"];
            string searchapiurl = SystemsManagerConfigurationProviderWithReloadExtensions.GetConfiguration(_configuration, "searchapiurl");
            return await _httpClient.GetStringAsync($"{searchapiurl}{searchUri}");
        }

        [HttpGet("housekeeping")]
        public async Task<IActionResult> HouseKeeping()
        {
            _logger.LogInformation("In Housekeeping, trying to reset the app.");
            
            // Add custom attributes to the current activity
            Activity currentActivity = Activity.Current;
            if (currentActivity != null)
            {
                currentActivity.SetTag("operation", "reset_app");
            }

            //string cleanupadoptionsurl = _configuration["cleanupadoptionsurl"];
            string cleanupadoptionsurl = SystemsManagerConfigurationProviderWithReloadExtensions.GetConfiguration(_configuration, "cleanupadoptionsurl");

            if (currentActivity != null)
            {
                currentActivity.SetTag("url", cleanupadoptionsurl);
            }
            
            await _httpClient.PostAsync(cleanupadoptionsurl, null);
            _logger.LogInformation("Cleanup adoptions completed successfully");

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Index(string selectedPetType, string selectedPetColor, string petid)
        {
            _logger.LogInformation("Searching pets with PetType:{PetType}, PetColor:{PetColor}, PetId:{PetId}",
                selectedPetType, selectedPetColor, petid);
                
            // Add custom attributes to the current activity
            Activity currentActivity = Activity.Current;
            if (currentActivity != null)
            {
                currentActivity.SetTag("pet.type", selectedPetType);
                currentActivity.SetTag("pet.color", selectedPetColor);
                currentActivity.SetTag("pet.id", petid);
            }

            string result;
            try
            {
                result = await GetPetDetails(selectedPetType, selectedPetColor, petid);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to retrieve pet details for PetType:{PetType}, PetColor:{PetColor}, PetId:{PetId}",
                    selectedPetType, selectedPetColor, petid);
                if (currentActivity != null)
                {
                    currentActivity.AddTag("error", true);
                    currentActivity.AddTag("error.message", e.Message);
                }
                throw;
            }

            var Pets = JsonSerializer.Deserialize<List<Pet>>(result);
            var PetDetails = new PetDetails()
            {
                Pets = Pets,
                Varieties = new Variety
                {
                    PetTypes = _variety.PetTypes,
                    PetColors = _variety.PetColors,
                    SelectedPetColor = selectedPetColor,
                    SelectedPetType = selectedPetType
                }
            };

            _logger.LogInformation("Retrieved {PetCount} pets", Pets.Count);

            PetsWaitingForAdoption.Set(Pets.Where(pet => pet.availability == "yes").Count());
            return View(PetDetails);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            _logger.LogError("Error occurred, RequestId: {RequestId}", Activity.Current?.Id ?? HttpContext.TraceIdentifier);
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
