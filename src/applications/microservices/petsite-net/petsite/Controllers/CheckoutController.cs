using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using PetSite.Helpers;
using PetSite.ViewModels;
using System;
using PetSite.Configuration;

namespace PetSite.Controllers
{
    public class CheckoutController : BaseController
    {
        private readonly ILogger<CheckoutController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ParameterRefreshManager _refreshManager;

        public CheckoutController(ILogger<CheckoutController> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration, ParameterRefreshManager refreshManager)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _refreshManager = refreshManager;
        }

        [HttpGet("/cart")]
        public async Task<ActionResult> Index([FromQuery] string userId)
        {
            if (EnsureUserId()) return new EmptyResult();

            try
            {
                var cartData = await GetCartDataAsync(userId);
                return View(cartData);
            }
            catch (Exception ex)
            {
                SpanExceptions.Record(ex);
                _logger.LogError(ex, $"Error fetching cart data for user: {userId}");
                ViewBag.ErrorMessage = $"Unable to load cart data at this time. Please try again later.\nError message: {ex.Message}";
                return View("Error", new PetSite.Models.ErrorViewModel { RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier });
            }
        }

        [HttpPost("/cart/checkout")]
        public async Task<IActionResult> PayAndCheckOut([FromBody] JsonElement requestData)
        {
            string userId = string.Empty;
            try
            {
                userId = requestData.GetProperty("userId").GetString();
                TagUserId(userId);

                using var httpClient = _httpClientFactory.CreateClient();
                var cartApiUrl = await ParameterNames.GetParameterValueAsync(ParameterNames.CART_API_URL, _refreshManager);
                var paymentUrl = UrlHelper.BuildUrl(cartApiUrl, new[] {  userId, "checkout" }, null);
                var jsonContent = new StringContent(requestData.GetRawText(), Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(paymentUrl, jsonContent);

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var orderData = JsonSerializer.Deserialize<JsonElement>(responseContent);

                    // Clear cart after successful payment
                    await ClearCartAsync(userId);

                    return Ok(new
                    {
                        success = true,
                        orderId = orderData.GetProperty("order_id").GetString(),
                        status = orderData.GetProperty("status").GetString(),
                        createdDate = DateTime.Parse(orderData.GetProperty("created_at").GetString()).ToString("MM/dd/yyyy"),
                        deliveryDate = DateTime.Parse(orderData.GetProperty("estimated_delivery").GetString()).ToString("MM/dd/yyyy")
                    });
                }

                var failure = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException(
                    $"Checkout failed for user {userId}: petfood returned {(int)response.StatusCode} {failure}",
                    null,
                    response.StatusCode);
            }
            catch (Exception ex)
            {
                SpanExceptions.Record(ex);
                _logger.LogError(ex, $"Payment failed for user: {userId}");
                return BadRequest($"Payment processing failed. Please try again.\nError: {ex.Message}");
            }
        }

        [HttpPost("/cart/clear")]
        public async Task<IActionResult> ClearCart(string userId)
        {
            TagUserId(userId);
            try
            {
                await ClearCartAsync(userId);
                return RedirectToAction("Index", new { userId });
            }
            catch (Exception ex)
            {
                SpanExceptions.Record(ex);
                _logger.LogError(ex, $"Failed to clear cart for user: {userId}");
                ViewBag.ErrorMessage = $"Unable to clear cart. Please try again later.\nError: {ex.Message}";
                return RedirectToAction("Index", new { userId });
            }
        }

        [HttpDelete("/cart/items/{food_id}")]
        public async Task<IActionResult> RemoveItem([FromRoute(Name = "food_id")] string foodId, [FromQuery] string userId)
        {
            TagUserId(userId);
            try
            {
                using var httpClient = _httpClientFactory.CreateClient();
                var cartApiUrl = await ParameterNames.GetParameterValueAsync(ParameterNames.CART_API_URL, _refreshManager);
                var removeItemUrl = UrlHelper.BuildUrl(cartApiUrl, new[] { userId, "items", foodId }, null);

                var response = await httpClient.DeleteAsync(removeItemUrl);
                response.EnsureSuccessStatusCode();

                var cartData = await GetCartDataAsync(userId);
                return Ok(cartData);
            }
            catch (Exception ex)
            {
                SpanExceptions.Record(ex);
                _logger.LogError(ex, "Failed to remove item from cart");
                return BadRequest($"Failed to remove item from cart. Error: {ex.Message}");
            }
        }

        private async Task<CartResponse> GetCartDataAsync(string userId)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            var cartApiUrl = await ParameterNames.GetParameterValueAsync(ParameterNames.CART_API_URL, _refreshManager);
            var cartUrl = UrlHelper.BuildUrl(cartApiUrl, new[] { userId }, null);
            var response = await httpClient.GetAsync(cartUrl);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<CartResponse>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        private async Task ClearCartAsync(string userId)
        {
            try
            {
                _logger.LogInformation($"Clearing cart for user: {userId}");
                using var httpClient = _httpClientFactory.CreateClient();
                var cartApiUrl = await ParameterNames.GetParameterValueAsync(ParameterNames.CART_API_URL, _refreshManager);
                var clearCartUrl = UrlHelper.BuildUrl(cartApiUrl, new[] { userId, "items" }, null);
                await httpClient.DeleteAsync(clearCartUrl);
                _logger.LogInformation($"Cart cleared for user: {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to clear cart for user: {userId}");
                throw;
            }
        }
    }
}
