using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PetSite.Helpers;

using PetSite.ViewModels;


namespace PetSite.Controllers
{
    public class AdoptionController : BaseController
    {
        private readonly PetSite.Services.IPetSearchService _petSearchService;
        private static Variety _variety = new Variety();
        private readonly ILogger<AdoptionController> _logger;

        public AdoptionController(ILogger<AdoptionController> logger, PetSite.Services.IPetSearchService petSearchService)
        {
            _petSearchService = petSearchService;
            _logger = logger;
        }

        [HttpGet("/pets/{pet_id}")]
        public async Task<IActionResult> Index([FromQuery] string userId, [FromRoute(Name = "pet_id")] string petId)
        {
            if (EnsureUserId()) return new EmptyResult(); // Redirect happened, stop processing

            Pet pet;
            ViewBag.UserId = userId;

            try
            {
                _logger.LogInformation($"Fetching pet details for pet_id: {petId}, user: {userId}");
                pet = await _petSearchService.GetPet(petId, userId);

                if (pet != null)
                {
                    _logger.LogInformation($"Retrieved pet details for pet_id {petId}: {JsonSerializer.Serialize(pet)}");
                }
                else
                {
                    _logger.LogWarning($"No pet found for pet_id: {petId}");
                    pet = new Pet();
                }
            }
            catch (Exception e)
            {
                SpanExceptions.Record(e);
                _logger.LogError(e, $"Error fetching pet details for pet_id: {petId}, user: {userId}");
                pet = new Pet();
                ViewBag.ErrorMessage = "Unable to load pet details. Please try again.";
            }

            return View(pet);
        }

        [HttpPost("/pets/{pet_id}/adopt")]
        public async Task<IActionResult> TakeMeHome([FromRoute(Name = "pet_id")] string petId,
            [FromForm(Name = "pet_type")] string petType, [FromForm(Name = "pet_color")] string petColor, string userId)
        {
            if(string.IsNullOrEmpty(userId)) EnsureUserId();
            TagUserId(userId);

            var currentActivity = Activity.Current;
            if (currentActivity != null)
            {
                currentActivity.SetTag("pet.id", petId);
                currentActivity.SetTag("pet.type", petType);
                currentActivity.SetTag("pet.color", petColor);

                _logger.LogInformation($"Processing adoption request - PetId:{petId}, PetType:{petType}, PetColor:{petColor} - for user: {userId}");
            }

            Pet selectedPet;

            try
            {
                using (var activity = Activity.Current?.Source?.StartActivity("Calling Search API"))
                {
                    if (activity != null)
                    {
                        activity.SetTag("pet.id", petId);
                        activity.SetTag("pet.type", petType);
                        activity.SetTag("pet.color", petColor);
                    }
                    _logger.LogInformation($"Inside TakeMeHome with - pet_type: {petType}, pet_color: {petColor}, pet_id: {petId} - for user: {userId}");
                    selectedPet = await _petSearchService.GetPet(petId, userId);
                }
            }
            catch (Exception e)
            {
                SpanExceptions.Record(e);
                _logger.LogError(e, $"Error calling PetSearch API for user: {userId}");
                ViewBag.ErrorMessage = $"Unable to process adoption request at this time. Please try again later.\nError message: {e.Message}";
                return View("Error", new PetSite.Models.ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
            }

            if (selectedPet != null)
            {
                _logger.LogInformation($"Redirecting to Index with pet_id: {selectedPet.petid} for user: {userId}");
                return RedirectToAction("Index", new { pet_id = selectedPet.petid, userId = userId });
            }

            return RedirectToAction("Index", "Home", new { userId = userId });
        }
    }
}
