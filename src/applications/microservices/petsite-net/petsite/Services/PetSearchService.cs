using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PetSite.ViewModels;
using PetSite.Helpers;
using Prometheus;
using PetSite.Configuration;

namespace PetSite.Services
{
    public interface IPetSearchService
    {
        Task<List<Pet>> SearchPets(string petType, string petColor, string userId);
        Task<Pet> GetPet(string petId, string userId);
    }

    public class PetSearchService : IPetSearchService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<PetSearchService> _logger;
        private readonly ParameterRefreshManager _refreshManager;

        //Prometheus metrics
        private static readonly Counter PetSearchCount =
            Metrics.CreateCounter("petsite_petsearches_total", "Count the number of searches performed");
        private static readonly Counter PuppySearchCount =
            Metrics.CreateCounter("petsite_pet_puppy_searches_total", "Count the number of puppy searches performed");
        private static readonly Counter KittenSearchCount =
            Metrics.CreateCounter("petsite_pet_kitten_searches_total", "Count the number of kitten searches performed");
        private static readonly Counter BunnySearchCount =
            Metrics.CreateCounter("petsite_pet_bunny_searches_total", "Count the number of bunny searches performed");

        public PetSearchService(IHttpClientFactory httpClientFactory, ILogger<PetSearchService> logger, ParameterRefreshManager refreshManager)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _refreshManager = refreshManager;
        }

        public async Task<List<Pet>> SearchPets(string petType, string petColor, string userId)
        {
            switch (petType)
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

            string searchApiUrl = await ParameterNames.GetParameterValueAsync(ParameterNames.SEARCH_API_URL, _refreshManager);
            var url = UrlHelper.BuildUrl(searchApiUrl, null,
                ("pet_type", petType != "all" ? petType : null),
                ("pet_color", petColor != "all" ? petColor : null),
                ("user_id", userId));

            var json = await GetAsync(url, allowNotFound: false);
            if (string.IsNullOrEmpty(json))
                return new List<Pet>();

            return JsonSerializer.Deserialize<List<Pet>>(json) ?? new List<Pet>();
        }

        public async Task<Pet> GetPet(string petId, string userId)
        {
            string searchApiUrl = await ParameterNames.GetParameterValueAsync(ParameterNames.SEARCH_API_URL, _refreshManager);
            var url = UrlHelper.BuildUrl(searchApiUrl, new[] { Uri.EscapeDataString(petId) }, ("user_id", userId));

            var json = await GetAsync(url, allowNotFound: true);
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<Pet>(json);
        }

        private async Task<string> GetAsync(string url, bool allowNotFound)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            try
            {
                _logger.LogInformation($"Calling the PetSearch API with: {url}");

                var response = await httpClient.GetAsync(url);
                if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                    return null;

                if (!response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.StatusCode}: {response.ReasonPhrase}. Response: {responseContent}");
                }

                var jsonContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"PetSearch API responded with: {jsonContent}");
                return jsonContent;
            }
            catch (Exception ex)
            {
                SpanExceptions.Record(ex);
                _logger.LogError(ex, "Exception occurred while fetching pet details.");
                throw;
            }
        }
    }
}
