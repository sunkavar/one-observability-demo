using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using PetSite.Helpers;
using PetSite.ViewModels;
using PetSite.Services;
using System.Text.Json;
using System.Linq;
using System.Text;
using Prometheus;
using PetSite.Configuration;

namespace PetSite.Controllers
{
    public class PaymentController : BaseController
    {
        private static string _txStatus = String.Empty;

        private readonly ILogger<PaymentController> _logger;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IPetSearchService _petSearchService;
        private readonly ParameterRefreshManager _refreshManager;

        //Prometheus metric to count the number of Pets adopted
        private static readonly Counter PetAdoptionCount =
            Metrics.CreateCounter("petsite_petadoptions_total", "Count the number of Pets adopted");

        public PaymentController(ILogger<PaymentController> logger, IConfiguration configuration,
            IHttpClientFactory httpClientFactory, IPetSearchService petSearchService, ParameterRefreshManager refreshManager)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _petSearchService = petSearchService;
            _logger = logger;
            _refreshManager = refreshManager;
        }

        [HttpGet("/adoptions/{pet_id}/confirmation")]
        public async Task<ActionResult> Index([FromQuery] string userId, string status,
            [FromRoute(Name = "pet_id")] string petId, [FromQuery(Name = "pet_type")] string petType)
        {
            if (EnsureUserId()) return new EmptyResult();

            ViewData["txStatus"] = status;

            Pet petDetails = null;
            if (!string.IsNullOrEmpty(petId) && !string.IsNullOrEmpty(petType))
            {
                try
                {
                    petDetails = await _petSearchService.GetPet(petId, userId);
                }
                catch (Exception ex)
                {
                    SpanExceptions.Record(ex);
                    _logger.LogError(ex, "Error fetching pet details after payment.");
                    ViewBag.ErrorMessage = $"Unable to load payment details at this time. Please try again later.\nError message: {ex.Message}";
                    return View("Error", new PetSite.Models.ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
                }
            }
            ViewData["PetDetails"] = petDetails;
            return View();
        }

        [HttpPost("/adoptions")]
        public async Task<IActionResult> MakePayment([FromForm(Name = "pet_id")] string petId,
            [FromForm(Name = "pet_type")] string petType, string userId)
        {
            if (string.IsNullOrEmpty(userId)) EnsureUserId();

            var currentActivity = Activity.Current;
            if (currentActivity != null)
            {
                currentActivity.SetTag("pet.id", petId);
                currentActivity.SetTag("pet.type", petType);

                _logger.LogInformation($"Inside MakePayment Action method - PetId:{petId} - PetType:{petType}");
            }

            try
            {
                using (var activity = Activity.Current?.Source?.StartActivity("Calling Payment API"))
                {
                    if (activity != null)
                    {
                        activity.SetTag("pet.id", petId);
                        activity.SetTag("pet.type", petType);
                    }

                    using var httpClient = _httpClientFactory.CreateClient();

                    var paymentapiurl = await ParameterNames.GetParameterValueAsync(ParameterNames.PAYMENT_API_URL, _refreshManager);
                    var url = UrlHelper.BuildUrl(paymentapiurl, null, null);
                    var body = JsonSerializer.Serialize(new { pet_id = petId, pet_type = petType, user_id = userId });
                    await httpClient.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
                }

                PetAdoptionCount.Inc();
                return RedirectToAction("Index", new { pet_id = petId, userId = userId, status = "success", pet_type = petType });
            }
            catch (Exception ex)
            {
                SpanExceptions.Record(ex);
                _logger.LogError(ex, $"Error in MakePayment: {ex.Message} - for user: {userId}");

                return RedirectToAction("Index", new { pet_id = petId, userId = userId, status = ex.Message });
            }
        }
    }
}
