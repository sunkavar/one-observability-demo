using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PetSite.Models;
using System.Net.Http;
using System.Text.Json;
using PetSite.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Configuration;

namespace PetSite.Controllers
{
    public class PetListAdoptionsController : Controller
    {
        private static HttpClient _httpClient;
        private IConfiguration _configuration;
        
        private readonly ILogger<PetListAdoptionsController> _logger;

        public PetListAdoptionsController(IConfiguration configuration, ILogger<PetListAdoptionsController> logger)
        {
            _configuration = configuration;
            
            // Initialize HttpClient once, relying on auto-instrumentation
            if (_httpClient == null)
            {
                _httpClient = new HttpClient();
            }
            _logger = logger;
        }

        // GET
        public async Task<IActionResult> Index()
        {
            _logger.LogInformation("Calling PetListAdoptions API");

            // Add custom attributes to the current activity
            Activity currentActivity = Activity.Current;
            if (currentActivity != null)
            {
                currentActivity.SetTag("operation", "list_adoptions");
                currentActivity.SetTag("service", "pet_list_adoptions");
            }

            string result;
            List<Pet> Pets = new List<Pet>();

            try
            {
                string petlistadoptionsurl = SystemsManagerConfigurationProviderWithReloadExtensions.GetConfiguration(_configuration, "petlistadoptionsurl");

                if (currentActivity != null)
                {
                    currentActivity.SetTag("url", petlistadoptionsurl);
                }
                
                result = await _httpClient.GetStringAsync($"{petlistadoptionsurl}");
                Pets = JsonSerializer.Deserialize<List<Pet>>(result);
                _logger.LogInformation("Retrieved {PetCount} adoptions from PetListAdoptions API", Pets.Count);
                
                if (currentActivity != null)
                {
                    currentActivity.SetTag("pet.count", Pets.Count);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to retrieve adoptions from PetListAdoptions API");
                
                if (currentActivity != null)
                {
                    currentActivity.SetTag("error", true);
                    currentActivity.SetTag("error.message", e.Message);
                }
                
                throw;
            }

            return View(Pets);
        }
    }
}
