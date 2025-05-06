using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
// Xray-To-Otel using Amazon.XRay.Recorder.Core;
// Xray-To-Otel using Amazon.XRay.Recorder.Handlers.AwsSdk;
// Xray-To-Otel using Amazon.XRay.Recorder.Handlers.System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Amazon.SQS;
using Amazon.SQS.Model;
using System.Text.Json.Serialization;
using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Microsoft.Extensions.Configuration;
using PetSite.Models;
using Prometheus;
using Newtonsoft;
using OpenTelemetry.Trace; // Add OTEL
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using static PetSite.Instrumentation;

namespace PetSite.Controllers
{
    public class PaymentController : Controller
    {
        private static string _txStatus = String.Empty;

        // Xray-To-Otel private static HttpClient _httpClient =
        // Xray-To-Otel   new HttpClient(new HttpClientXRayTracingHandler(new HttpClientHandler()));

        private static HttpClient _httpClient = new HttpClient();

        private static AmazonSQSClient _sqsClient;
        private static IConfiguration _configuration;

        private readonly ActivitySource _activitySource;

        private readonly ILogger<PaymentController> _logger;

        //Prometheus metric to count the number of Pets adopted
        private static readonly Counter PetAdoptionCount =
            Metrics.CreateCounter("petsite_petadoptions_total", "Count the number of Pets adopted");

        public PaymentController(IConfiguration configuration, Instrumentation instrumentation, ILogger<PaymentController> logger)
        {
            // Xray-To-Otel AWSSDKHandler.RegisterXRayForAllServices();
            _configuration = configuration;

            _sqsClient = new AmazonSQSClient(Amazon.Util.EC2InstanceMetadata.Region);
            _activitySource = instrumentation.ActivitySource;
            _logger = logger;
        }

        // GET: Payment
        [HttpGet]
        private ActionResult Index()
        {
            return View();
        }

        // POST: Payment/MakePayment
        [HttpPost]
        // [ValidateAntiForgeryToken]
        public async Task<IActionResult> MakePayment(string petId, string pettype)
        {
            // Xray-To-Otel AWSXRayRecorder.Instance.AddMetadata("PetType", pettype);
            // Xray-To-Otel AWSXRayRecorder.Instance.AddMetadata("PetId", petId);

            ViewData["txStatus"] = "success";

            try
            {
                // Replace X-Ray subsegment with OTEL
                // Xray-To-Otel AWSXRayRecorder.Instance.BeginSubsegment("Call Payment API");
                using (var activity = _activitySource.StartActivity("CallPaymentAPI"))
                {
                    activity?.SetTag("pet.id", petId);
                    activity?.SetTag("pet.type", pettype);

                    var result = await PostTransaction(petId, pettype);
                    // Xray-To-Otel AWSXRayRecorder.Instance.EndSubsegment(); // Handled by using block

                    _logger.LogInformation("Payment API called successfully for PetId:{PetId}, PetType:{PetType}", petId, pettype);
                }

                using (var activity = _activitySource.StartActivity("PostMessageToSQS"))
                {
                    var messageResponse = await PostMessageToSqs(petId, pettype);
                    _logger.LogInformation("Message posted to SQS for PetId:{PetId}, PetType:{PetType}", petId, pettype);
                }

                using (var activity = _activitySource.StartActivity("SendNotification"))
                {
                    var snsResponse = await SendNotification(petId);
                    _logger.LogInformation("Notification sent for PetId:{PetId}", petId);
                }

                if ("bunny" == pettype)
                {
                    using (var activity = _activitySource.StartActivity("StartStepFunction"))
                    {
                        var stepFunctionResult = await StartStepFunctionExecution(petId, pettype);
                        _logger.LogInformation("Step Function started for PetId:{PetId}, PetType:{PetType}", petId, pettype);
                    }
                }

                PetAdoptionCount.Inc();
                _logger.LogInformation("Pet adoption count incremented for PetId:{PetId}", petId);
                return View("Index");
            }
            catch (Exception ex)
            {
                ViewData["txStatus"] = "failure";
                ViewData["error"] = ex.Message;
                _logger.LogError(ex, "Payment process failed for PetId:{PetId}, PetType:{PetType}", petId, pettype);
                // Xray-To-Otel AWSXRayRecorder.Instance.AddException(ex); // Comment out
                Activity.Current?.AddException(ex); // OTEL exception tracking
                return View("Index");
            }
        }

        private async Task<HttpResponseMessage> PostTransaction(string petId, string pettype)
        {
            return await _httpClient.PostAsync($"{SystemsManagerConfigurationProviderWithReloadExtensions.GetConfiguration(_configuration, "paymentapiurl")}?petId={petId}&petType={pettype}",
                null);
        }

        private async Task<SendMessageResponse> PostMessageToSqs(string petId, string petType)
        {
            // Xray-To-Otel AWSSDKHandler.RegisterXRay<IAmazonSQS>();

            return await _sqsClient.SendMessageAsync(new SendMessageRequest()
            {
                MessageBody = JsonSerializer.Serialize($"{petId}-{petType}"),
                QueueUrl = SystemsManagerConfigurationProviderWithReloadExtensions.GetConfiguration(_configuration, "queueurl")
            });
        }

        private async Task<StartExecutionResponse> StartStepFunctionExecution(string petId, string petType)
        {
            /*
             
             // Code to invoke StepFunction through API Gateway
             var stepFunctionInputModel = new StepFunctionInputModel()
            {
                input = JsonSerializer.Serialize(new SearchParams() {petid = petId, pettype = petType}),
                name = $"{petType}-{petId}-{Guid.NewGuid()}",
                stateMachineArn = SystemsManagerConfigurationProviderWithReloadExtensions.GetConfiguration(_configuration,"petadoptionsstepfnarn")
            };
            
            var content = new StringContent(
                JsonSerializer.Serialize(stepFunctionInputModel),
                Encoding.UTF8,
                "application/json");

            return await _httpClient.PostAsync(SystemsManagerConfigurationProviderWithReloadExtensions.GetConfiguration(_configuration,"petadoptionsstepfnurl"), content);
            
            */
            // Console.WriteLine($"STEPLOG -ARN - {SystemsManagerConfigurationProviderWithReloadExtensions.GetConfiguration(_configuration,"petadoptionsstepfnarn")}");
            //Console.WriteLine($"STEPLOG - SERIALIZE - {JsonSerializer.Serialize(new SearchParams() {petid = petId, pettype = petType})}");


            // Xray-To-Otel AWSSDKHandler.RegisterXRay<IAmazonStepFunctions>();
            return await new AmazonStepFunctionsClient().StartExecutionAsync(new StartExecutionRequest()
            {
                Input = JsonSerializer.Serialize(new SearchParams() { petid = petId, pettype = petType }),
                Name = $"{petType}-{petId}-{Guid.NewGuid()}",
                StateMachineArn = SystemsManagerConfigurationProviderWithReloadExtensions.GetConfiguration(_configuration, "petadoptionsstepfnarn")
            });
        }

        private async Task<PublishResponse> SendNotification(string petId)
        {
            // Xray-To-Otel  AWSSDKHandler.RegisterXRay<IAmazonService>();

            var snsClient = new AmazonSimpleNotificationServiceClient();
            return await snsClient.PublishAsync(topicArn: _configuration["snsarn"],
                message: $"PetId {petId} was adopted on {DateTime.Now}");

        }
    }
}