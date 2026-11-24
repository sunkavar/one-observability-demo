package payforadoption

import (
	"context"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/go-kit/log"
)

func TestDecodeCompleteAdoptionRequest(t *testing.T) {
	body := `{"pet_id":"p1","pet_type":"dog","user_id":"u1"}`
	r := httptest.NewRequest(http.MethodPost, "/adoptions", strings.NewReader(body))
	got, err := decodeCompleteAdoptionRequest(context.Background(), r)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	req := got.(completeAdoptionRequest)
	if req.PetId != "p1" || req.PetType != "dog" || req.UserID != "u1" {
		t.Errorf("unexpected request: %+v", req)
	}
}

func TestDecodeCompleteAdoptionRequestInvalid(t *testing.T) {
	for _, body := range []string{`{"pet_id":"p1","pet_type":"dog"}`, `not json`} {
		r := httptest.NewRequest(http.MethodPost, "/adoptions", strings.NewReader(body))
		if _, err := decodeCompleteAdoptionRequest(context.Background(), r); err != ErrBadRequest {
			t.Errorf("body %q: expected ErrBadRequest, got %v", body, err)
		}
	}
}

func TestDecodeCleanupAdoptionsRequest(t *testing.T) {
	r := httptest.NewRequest(http.MethodDelete, "/adoptions?user_id=u1", nil)
	got, err := decodeCleanupAdoptionsRequest(context.Background(), r)
	if err != nil || got.(cleanupAdoptionsRequest).UserID != "u1" {
		t.Errorf("got %+v, err %v", got, err)
	}
	r = httptest.NewRequest(http.MethodDelete, "/adoptions", nil)
	if _, err := decodeCleanupAdoptionsRequest(context.Background(), r); err != ErrBadRequest {
		t.Errorf("expected ErrBadRequest, got %v", err)
	}
}

func TestRoutes(t *testing.T) {
	h := MakeHTTPHandler(nil, log.NewNopLogger())
	for _, c := range []struct{ method, path string }{
		{"POST", "/api/completeadoption"},
		{"DELETE", "/api/cleanupadoptions/u1"},
		{"POST", "/api/triggerseeding"},
	} {
		w := httptest.NewRecorder()
		h.ServeHTTP(w, httptest.NewRequest(c.method, c.path, nil))
		if w.Code != http.StatusNotFound {
			t.Errorf("%s %s: expected 404, got %d", c.method, c.path, w.Code)
		}
	}
}
