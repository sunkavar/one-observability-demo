using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PetSite.Controllers
{
    public class PetFoodController : Controller
    {
        private static HttpClient httpClient;
        private IConfiguration _configuration;
        
        private readonly ILogger<PetFoodController> _logger;

        public PetFoodController(IConfiguration configuration, ILogger<PetFoodController> logger)
        {
            _configuration = configuration;

            // Initialize HttpClient once, relying on auto-instrumentation
            if (httpClient == null)
            {
                httpClient = new HttpClient();
            }

            _logger = logger;
        }

        [HttpGet("/petfood")]
        public async Task<string> Index()
        {
            _logger.LogInformation("Calling PetFood service");

            // Add custom attributes to the current activity
            Activity currentActivity = Activity.Current;
            if (currentActivity != null)
            {
                currentActivity.SetTag("service", "petfood");
                currentActivity.SetTag("operation", "get_petfood");
            }

            // Get our data from petfood
            string result = await httpClient.GetStringAsync("http://petfood");

            // Return the result!
            _logger.LogInformation("PetFood service returned: {Result}", result);
            return result;
        }

        [HttpGet("/petfood-metric/{entityId}/{value}")]
        public async Task<string> PetFoodMetric(string entityId, float value)
        {
            _logger.LogInformation("Calling PetFood metric with EntityId:{EntityId}, Value:{Value}", entityId, value);

            // Add custom attributes to the current activity
            Activity currentActivity = Activity.Current;
            if (currentActivity != null)
            {
                currentActivity.SetTag("service", "petfood-metric");
                currentActivity.SetTag("entity.id", entityId);
                currentActivity.SetTag("metric.value", value);
                currentActivity.SetTag("operation", "get_petfood_metric");
            }

            string result = await httpClient.GetStringAsync("http://petfood-metric/metric/" + entityId + "/" + value.ToString());

            _logger.LogInformation("PetFood metric returned: {Result}", result);
            return result;
        }
    }
}
