using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;

namespace PetSite.Controllers
{
    public class PetFoodController : Controller
    {
        private readonly HttpClient _httpClient;
        private IConfiguration _configuration;
        
        public PetFoodController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _configuration = configuration;
            _httpClient = httpClientFactory.CreateClient();
        }

        [HttpGet("/petfood")]
        public async Task<string> Index()
        {
            // Add custom span attributes using Activity API
            var currentActivity = Activity.Current;
            if (currentActivity != null)
            {
                currentActivity.SetTag("Operation", "GetPetFood");
                Console.WriteLine("Calling PetFood");
            }
            
            string result;
            
            try
            {
                // Create a new activity for the API call
                using (var activity = new Activity("Calling PetFood").Start())
                {
                    if (activity != null)
                    {
                        activity.SetTag("Operation", "GetPetFood");
                    }
                    
                    // Get our data from petfood
                    result = await _httpClient.GetStringAsync("http://petfood");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error calling PetFood: {e.Message}");
                throw;
            }
            
            // Return the result!
            return result;
        }
        
        [HttpGet("/petfood-metric/{entityId}/{value}")]
        public async Task<string> PetFoodMetric(string entityId, float value)
        {
            // Add custom span attributes using Activity API
            var currentActivity = Activity.Current;
            if (currentActivity != null)
            {
                currentActivity.SetTag("Operation", "PetFoodMetric");
                currentActivity.SetTag("EntityId", entityId);
                currentActivity.SetTag("Value", value.ToString());
                
                Console.WriteLine("Calling: " + "http://petfood-metric/metric/" + entityId + "/" + value.ToString());
            }
            
            string result;
            
            try
            {
                // Create a new activity for the API call
                using (var activity = new Activity("Calling PetFood metric").Start())
                {
                    if (activity != null)
                    {
                        activity.SetTag("Operation", "PetFoodMetric");
                        activity.SetTag("EntityId", entityId);
                        activity.SetTag("Value", value.ToString());
                    }
                    
                    result = await _httpClient.GetStringAsync("http://petfood-metric/metric/" + entityId + "/" + value.ToString());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error calling PetFood metric: {e.Message}");
                throw;
            }
            
            return result;
        }
    }
}
