/*
Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
SPDX-License-Identifier: Apache-2.0
*/

package ca.petsearch.controllers;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * Strips denied terms from text shown to customers. Off unless PETSEARCH_FAULT=cpu-regex: this is the
 * deliberate CPU regression the daily profiles use case injects.
 */
final class PetTextFilter {

    static final boolean ENABLED = "cpu-regex".equals(System.getenv("PETSEARCH_FAULT"));

    private static final List<String> DENIED_TERMS = deniedTerms(intFromEnv("PETSEARCH_FAULT_TERMS", 3000));

    private PetTextFilter() {}

    static String clean(String text) {
        if (!ENABLED || text == null) {
            return text;
        }
        String result = text;
        for (String term : DENIED_TERMS) {
            // String.replaceAll compiles a new Pattern on every call.
            result = result.replaceAll("(?i)\\b" + term + "\\b", "");
        }
        return result;
    }

    private static List<String> deniedTerms(int count) {
        List<String> terms = new ArrayList<>(count);
        for (int i = 0; i < count; i++) {
            terms.add("denied" + i);
        }
        return Collections.unmodifiableList(terms);
    }

    private static int intFromEnv(String name, int fallback) {
        try {
            return Integer.parseInt(System.getenv(name));
        } catch (NumberFormatException e) {
            return fallback;
        }
    }
}
