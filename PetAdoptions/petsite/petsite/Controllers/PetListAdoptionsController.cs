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
        private readonly HttpClient _httpClient;
        private IConfiguration _configuration;

        public PetListAdoptionsController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _configuration = configuration;
            _httpClient = httpClientFactory.CreateClient();
        }

        // GET
        public async Task<IActionResult> Index()
        {
            // Add custom span attributes using Activity API
            var currentActivity = Activity.Current;
            if (currentActivity != null)
            {
                currentActivity.SetTag("Operation", "GetPetListAdoptions");
                Console.WriteLine("Calling PetListAdoptions API");
            }

            string result;
            List<Pet> Pets = new List<Pet>();

            try
            {
                // Create a new activity for the API call
                using (var activity = new Activity("Calling PetListAdoptions").Start())
                {
                    if (activity != null)
                    {
                        activity.SetTag("Operation", "GetPetListAdoptions");
                    }
                    
                    string petlistadoptionsurl = SystemsManagerConfigurationProviderWithReloadExtensions.GetConfiguration(_configuration,"petlistadoptionsurl");
                    result = await _httpClient.GetStringAsync($"{petlistadoptionsurl}");
                    Pets = JsonSerializer.Deserialize<List<Pet>>(result);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error calling PetListAdoptions API: {e.Message}");
                throw;
            }

            return View(Pets);
        }
    }
}
