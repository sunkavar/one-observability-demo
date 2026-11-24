/*
Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
SPDX-License-Identifier: Apache-2.0
*/
package ca.petsearch.controllers;

import ca.petsearch.MetricEmitter;
import ca.petsearch.ParameterRefreshManager;
import ca.petsearch.RandomNumberGenerator;
import com.amazonaws.services.dynamodbv2.AmazonDynamoDB;
import com.amazonaws.services.dynamodbv2.model.AttributeValue;
import com.amazonaws.services.dynamodbv2.model.ComparisonOperator;
import com.amazonaws.services.dynamodbv2.model.Condition;
import com.amazonaws.services.dynamodbv2.model.ScanRequest;
import com.amazonaws.services.s3.AmazonS3;
import com.amazonaws.services.simplesystemsmanagement.AWSSimpleSystemsManagement;
import com.amazonaws.services.simplesystemsmanagement.model.GetParameterRequest;
import com.amazonaws.services.simplesystemsmanagement.model.GetParameterResult;
import com.amazonaws.services.simplesystemsmanagement.model.ParameterNotFoundException;
import io.opentelemetry.api.trace.Span;
import io.opentelemetry.api.trace.Tracer;
import io.opentelemetry.context.Scope;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;

import java.util.*;
import java.util.concurrent.TimeUnit;
import java.util.stream.Collectors;

@RestController
public class SearchController {
    private final RandomNumberGenerator randomGenerator;

    // Configurable parameter names via environment variables
    private final String imagesCdnUrlParam;
    private final String s3BucketParam;
    private final String dynamodbTableParam;

    private Logger logger = LoggerFactory.getLogger(SearchController.class);

    private final AmazonS3 s3Client;
    private final AmazonDynamoDB ddbClient;
    private final AWSSimpleSystemsManagement ssmClient;
    private final MetricEmitter metricEmitter;
    private final Tracer tracer;
    private final ParameterRefreshManager refreshManager;

    public SearchController(AmazonS3 s3Client, AmazonDynamoDB ddbClient, AWSSimpleSystemsManagement ssmClient,
            MetricEmitter metricEmitter, Tracer tracer, RandomNumberGenerator randomGenerator,
            ParameterRefreshManager refreshManager) {
        this.s3Client = s3Client;
        this.ddbClient = ddbClient;
        this.ssmClient = ssmClient;
        this.metricEmitter = metricEmitter;
        this.tracer = tracer;
        this.randomGenerator = randomGenerator;
        this.refreshManager = refreshManager;

        // Initialize configurable parameter names from environment variables
        String paramPrefix = getRequiredEnvironmentVariable("PETSEARCH_PARAM_PREFIX");
        String s3BucketName = getRequiredEnvironmentVariable("PETSEARCH_S3_BUCKET_NAME");
        String dynamodbTableName = getRequiredEnvironmentVariable("PETSEARCH_DYNAMODB_TABLE_NAME");
        String imagesCdnUrl = getRequiredEnvironmentVariable("PETSEARCH_IMAGES_CDN_URL");

        this.imagesCdnUrlParam = paramPrefix + "/" + imagesCdnUrl;
        this.s3BucketParam = paramPrefix + "/" + s3BucketName;
        this.dynamodbTableParam = paramPrefix + "/" + dynamodbTableName;

        logger.info("Using SSM parameter names: ImagesCDN={}, S3Bucket={}, DynamoDBTable={}",
                   imagesCdnUrlParam, s3BucketParam, dynamodbTableParam);
    }

    /**
     * Get a required environment variable, throwing an exception if it's missing or empty
     */
    private String getRequiredEnvironmentVariable(String name) {
        String value = System.getenv(name);
        if (value == null || value.trim().isEmpty()) {
            throw new IllegalStateException("Required environment variable '" + name + "' is not set or is empty");
        }
        return value.trim();
    }

    private String getKey(String petType, String petId) {
        logger.debug("Generating S3 key for petType: '{}', petId: '{}'", petType, petId);

        String folderName;

        switch (petType) {
            case "bunny":
                folderName = "bunnies";
                break;
            case "puppy":
                folderName = "puppies";
                break;
            default:
                folderName = "kittens";
                break;
        }

        String key = String.format("%s/%s.jpg", folderName, petId);
        logger.debug("Generated S3 key '{}' for petType '{}', petId '{}'", key, petType, petId);
        return key;
    }

    private String getPetUrl(String petType, String image) {
        String cloudFrontUrl = String.format("%s/%s", getSSMParameter(imagesCdnUrlParam), getKey(petType, image));
        logger.debug("Generated CloudFront URL for petType '{}', image '{}': {}", petType, image, cloudFrontUrl);
        return cloudFrontUrl;
    }

    private String getSSMParameter(String paramName) {
        logger.info("Attempting to retrieve SSM parameter: {}", paramName);

        if (refreshManager.shouldRefresh(paramName)) {
            try {
                logger.debug("Parameter needs refresh, fetching from SSM: {}", paramName);

                GetParameterRequest parameterRequest = new GetParameterRequest().withName(paramName)
                        .withWithDecryption(false);

                GetParameterResult parameterResult = ssmClient.getParameter(parameterRequest);
                String paramValue = parameterResult.getParameter().getValue();

                logger.info("Successfully retrieved SSM parameter '{}' with value: {}", paramName,
                           paramValue != null ? paramValue.substring(0, Math.min(50, paramValue.length())) + "..." : "null");

                refreshManager.cacheParameter(paramName, paramValue);
                return paramValue;

            } catch (ParameterNotFoundException e) {
                logger.error("SSM Parameter NOT FOUND: '{}'. This parameter does not exist in Parameter Store.", paramName);
                logger.error("Available environment variables for debugging:");
                System.getenv().entrySet().stream()
                    .filter(entry -> entry.getKey().startsWith("PETSEARCH_"))
                    .forEach(entry -> logger.error("  {} = {}", entry.getKey(), entry.getValue()));

                // Add span attributes for better tracing
                Span currentSpan = Span.current();
                currentSpan.setAttribute("error", true);
                currentSpan.setAttribute("error.parameter_name", paramName);
                currentSpan.setAttribute("error.type", "ParameterNotFoundException");
                currentSpan.recordException(e);

                throw new RuntimeException("Failed to retrieve SSM parameter: " + paramName +
                                         ". Parameter does not exist in Parameter Store.", e);

            } catch (Exception e) {
                logger.error("Unexpected error retrieving SSM parameter '{}': {}", paramName, e.getMessage(), e);

                // Add span attributes for better tracing
                Span currentSpan = Span.current();
                currentSpan.setAttribute("error", true);
                currentSpan.setAttribute("error.parameter_name", paramName);
                currentSpan.setAttribute("error.type", e.getClass().getSimpleName());
                currentSpan.recordException(e);

                throw new RuntimeException("Failed to retrieve SSM parameter: " + paramName, e);
            }
        } else {
            logger.debug("Using cached value for SSM parameter: {}", paramName);
            return refreshManager.getCachedParameter(paramName);
        }
    }

    private Pet mapToPet(Map<String, AttributeValue> item) {
        String petId = item.get("petid").getS();
        String availability = PetTextFilter.clean(item.get("availability").getS());
        String cutenessRate = item.get("cuteness_rate").getS();
        String petColor = PetTextFilter.clean(item.get("petcolor").getS());
        String petType = item.get("pettype").getS();
        String price = item.get("price").getS();
        String petUrl = getPetUrl(petType, item.get("image").getS());

        logger.debug("Mapping DynamoDB item to Pet object - petId: '{}', petType: '{}', petColor: '{}', availability: '{}', cutenessRate: '{}', price: '{}'",
                    petId, petType, petColor, availability, cutenessRate, price);

        Pet currentPet = new Pet(petId, availability, cutenessRate, petColor, petType, price, petUrl);
        return currentPet;
    }

    @GetMapping("/pets")
    public ResponseEntity<?> listPets(
            @RequestParam(name = "pet_type", defaultValue = "", required = false) String petType,
            @RequestParam(name = "pet_color", defaultValue = "", required = false) String petColor,
            @RequestParam(name = "user_id", defaultValue = "", required = false) String userId)
            throws InterruptedException {
        Span.current().setAttribute("user.id", userId);
        logger.info("List pets request - userId: '{}', petType: '{}', petColor: '{}'", userId, petType, petColor);

        SearchQuery query = new SearchQuery(petType, petColor, "");
        String validatedPetType = query.getValidatedPetType();

        if (!SearchQuery.isEmptyParameter(validatedPetType) &&
            !validatedPetType.equals("puppy") && !validatedPetType.equals("kitten") && !validatedPetType.equals("bunny")) {
            String errorMsg = validatedPetType + " pet type not found";
            logger.warn("Invalid pet type requested by userId '{}' - petType: '{}', returning 404", userId, validatedPetType);
            Span span = tracer.spanBuilder("Scanning DynamoDB Table").startSpan();
            span.setAttribute("error", true);
            span.setAttribute("user.id", userId);
            span.setAttribute("error.message", errorMsg);
            span.end();
            return ResponseEntity.status(HttpStatus.NOT_FOUND)
                    .body(new ErrorResponse(404, errorMsg, errorMsg, "/pets"));
        }

        List<Pet> result = scanPets(query, userId);
        metricEmitter.emitPetsReturnedMetric(result.size());
        logger.info("List pets completed for userId '{}' - returning {} pets", userId, result.size());
        return ResponseEntity.ok(result);
    }

    @GetMapping("/pets/{pet_id}")
    public ResponseEntity<?> getPet(
            @PathVariable("pet_id") String petId,
            @RequestParam(name = "user_id", defaultValue = "", required = false) String userId)
            throws InterruptedException {
        Span.current().setAttribute("user.id", userId);
        logger.info("Get pet request - userId: '{}', petId: '{}'", userId, petId);

        List<Pet> result = scanPets(new SearchQuery("", "", petId), userId);
        metricEmitter.emitPetsReturnedMetric(result.size());
        if (result.isEmpty()) {
            String errorMsg = "pet " + petId + " not found";
            logger.warn("Pet not found for userId '{}' - petId: '{}', returning 404", userId, petId);
            return ResponseEntity.status(HttpStatus.NOT_FOUND)
                    .body(new ErrorResponse(404, errorMsg, errorMsg, "/pets/" + petId));
        }
        return ResponseEntity.ok(result.get(0));
    }

    private List<Pet> scanPets(SearchQuery query, String userId) throws InterruptedException {
        String petType = query.getValidatedPetType();
        String petColor = query.getNormalizedPetColor();
        String petId = query.getNormalizedPetId();

        Span span = tracer.spanBuilder("Scanning DynamoDB Table").startSpan();
        try (Scope scope = span.makeCurrent()) {
            // This line is intentional. Delays searches
            if (petType.equals("bunny")) {
                logger.debug("Delaying the response on purpose, to show on traces as an issue - userId: '{}'", userId);
                TimeUnit.MILLISECONDS.sleep(3000);
            }

            span.setAttribute("search.pettype", petType);
            span.setAttribute("search.petcolor", petColor);
            span.setAttribute("search.petid", petId);
            span.setAttribute("user.id", userId);

            List<Pet> result = ddbClient.scan(buildScanRequest(petType, petColor, petId))
                    .getItems().stream().map(this::mapToPet)
                    .collect(Collectors.toList());
            logger.info("DynamoDB scan completed for userId '{}' - found {} pets (petType: '{}', petColor: '{}', petId: '{}')",
                       userId, result.size(), petType, petColor, petId);
            return result;
        } catch (Exception e) {
            span.recordException(e);
            logger.error("Error while scanning pets for userId '{}' - petType: '{}', petColor: '{}', petId: '{}'",
                        userId, petType, petColor, petId, e);
            throw e;
        } finally {
            span.end();
        }
    }

    private ScanRequest buildScanRequest(String petType, String petColor, String petId) {
        return Map.of("pettype", petType,
                "petcolor", petColor,
                "petid", petId).entrySet().stream()
                .filter(e -> !isEmptyParameter(e))
                .map(this::entryToCondition)
                .reduce(emptyScanRequest(), this::addScanFilter, this::joinScanResult);
    }

    private ScanRequest addScanFilter(ScanRequest scanResult, Map.Entry<String, Condition> element) {
        return scanResult.addScanFilterEntry(element.getKey(), element.getValue());
    }

    private ScanRequest emptyScanRequest() {
        return new ScanRequest().withTableName(getSSMParameter(dynamodbTableParam));
    }

    private ScanRequest joinScanResult(ScanRequest scanRequest1, ScanRequest scanRequest2) {
        Map<String, Condition> merged = new HashMap<>();
        merged.putAll(scanRequest1.getScanFilter() != null ? scanRequest1.getScanFilter() : Collections.emptyMap());
        merged.putAll(scanRequest2.getScanFilter() != null ? scanRequest2.getScanFilter() : Collections.emptyMap());

        return scanRequest1.withScanFilter(merged);
    }

    private Map.Entry<String, Condition> entryToCondition(Map.Entry<String, String> e) {
        Span.current().setAttribute(e.getKey(), e.getValue());
        return Map.entry(e.getKey(), new Condition()
                .withComparisonOperator(ComparisonOperator.EQ)
                .withAttributeValueList(new AttributeValue().withS(e.getValue())));
    }

    private boolean isEmptyParameter(Map.Entry<String, String> e) {
        return e.getValue() == null || e.getValue().isEmpty();
    }

}
