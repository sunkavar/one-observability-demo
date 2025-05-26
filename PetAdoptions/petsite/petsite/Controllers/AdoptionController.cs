using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
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
        private static readonly HttpClient HttpClient = new HttpClient();
        private static Variety _variety = new Variety();
        private static IConfiguration _configuration;
        private static string _searchApiurl;
        
        private readonly ILogger<AdoptionController> _logger;

        public AdoptionController(IConfiguration configuration, ILogger<AdoptionController> logger)
        {
            _configuration = configuration;
            _searchApiurl = SystemsManagerConfigurationProviderWithReloadExtensions.GetConfiguration(_configuration, "searchapiurl");
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
            _logger.LogInformation("Inside TakeMeHome - PetId:{PetId}, PetType:{PetType}, PetColor:{PetColor}",
                searchParams.petid, searchParams.pettype, searchParams.petcolor);

            // Add custom attributes to the current activity
            Activity currentActivity = Activity.Current;
            if (currentActivity != null)
            {
                currentActivity.SetTag("pet.id", searchParams.petid);
                currentActivity.SetTag("pet.type", searchParams.pettype);
                currentActivity.SetTag("pet.color", searchParams.petcolor);
                currentActivity.SetTag("operation", "take_me_home");
            }

            string result;

            try
            {
                // Create a sub-span for the search API call
                using (var searchActivity = Activity.Current?.Source.StartActivity("Calling Search API"))
                {
                    if (searchActivity != null)
                    {
                        searchActivity.SetTag("url", _searchApiurl);
                    }
                    
                    result = await GetPetDetails(searchParams);
                    _logger.LogInformation("Successfully called search API for PetId:{PetId}", searchParams.petid);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to call search API for PetId:{PetId}, PetType:{PetType}",
                    searchParams.petid, searchParams.pettype);
                
                if (currentActivity != null)
                {
                    currentActivity.SetTag("error", true);
                    currentActivity.SetTag("error.message", e.Message);
                }
                
                throw;
            }

            return View("Index", JsonSerializer.Deserialize<List<Pet>>(result).FirstOrDefault());
        }
    }
}
