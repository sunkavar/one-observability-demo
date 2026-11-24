#!/bin/bash

# Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
# SPDX-License-Identifier: Apache-2.0

# Example script to test the payforadoption service with the user_id field

# Set the service URL (adjust as needed)
SERVICE_URL="http://localhost:80"

echo "Testing Complete Adoption API with User ID..."

# Test the complete adoption endpoint with all required parameters
curl -X POST "${SERVICE_URL}/adoptions" \
  -H "Content-Type: application/json" \
  -d '{"pet_id":"pet123","pet_type":"dog","user_id":"user456"}' \
  -w "\nHTTP Status: %{http_code}\n" \
  -s

echo -e "\n\nTesting Health Check..."

# Test health check
curl -X GET "${SERVICE_URL}/health/status" \
  -w "\nHTTP Status: %{http_code}\n" \
  -s

echo -e "\n\nTesting with missing user_id field (should return 400)..."

# Test with missing user_id field (should fail)
curl -X POST "${SERVICE_URL}/adoptions" \
  -H "Content-Type: application/json" \
  -d '{"pet_id":"pet123","pet_type":"dog"}' \
  -w "\nHTTP Status: %{http_code}\n" \
  -s

echo -e "\n\nTesting Error Mode scenarios (if enabled)..."

# Test different pet types to see various degradation scenarios
echo "Testing bunny (critical failure):"
curl -X POST "${SERVICE_URL}/adoptions" \
  -H "Content-Type: application/json" \
  -d '{"pet_id":"bunny1","pet_type":"bunny","user_id":"user1"}' \
  -w "\nHTTP Status: %{http_code}\n" \
  -s

echo -e "\nTesting dog (intermittent failures):"
curl -X POST "${SERVICE_URL}/adoptions" \
  -H "Content-Type: application/json" \
  -d '{"pet_id":"dog1","pet_type":"dog","user_id":"user2"}' \
  -w "\nHTTP Status: %{http_code}\n" \
  -s

echo -e "\nTesting cat (REAL database connection exhaustion - will impact database!):"
curl -X POST "${SERVICE_URL}/adoptions" \
  -H "Content-Type: application/json" \
  -d '{"pet_id":"cat1","pet_type":"cat","user_id":"user3"}' \
  -w "\nHTTP Status: %{http_code}\n" \
  -s

echo -e "\nTesting impact during connection exhaustion (try another request immediately):"
curl -X POST "${SERVICE_URL}/adoptions" \
  -H "Content-Type: application/json" \
  -d '{"pet_id":"dog2","pet_type":"dog","user_id":"user5"}' \
  -w "\nHTTP Status: %{http_code}\n" \
  -s

echo -e "\nTesting fish (partial degradation):"
curl -X POST "${SERVICE_URL}/adoptions" \
  -H "Content-Type: application/json" \
  -d '{"pet_id":"fish1","pet_type":"fish","user_id":"user4"}' \
  -w "\nHTTP Status: %{http_code}\n" \
  -s

echo -e "\n\nNote: Error mode scenarios only activate when /petstore/errormode1 parameter is set to 'true'"
echo "Done!"