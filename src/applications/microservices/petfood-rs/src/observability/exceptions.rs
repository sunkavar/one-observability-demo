use opentelemetry::{trace::TraceContextExt, KeyValue};
use std::{error::Error, fmt::Debug};
use tracing_opentelemetry::OpenTelemetrySpanExt;

/// Adds the OpenTelemetry `exception` event for `err` to the current span.
pub fn record_exception<E: Error + Debug>(err: &E, message: &str) {
    let context = tracing::Span::current().context();
    context.span().add_event(
        "exception",
        vec![
            KeyValue::new("exception.type", exception_type(err)),
            KeyValue::new("exception.message", message.to_string()),
        ],
    );
}

/// `ServiceError::ValidationError` style name: the type plus the enum variant.
fn exception_type<E: Debug>(err: &E) -> String {
    let full = std::any::type_name::<E>();
    let short = full.rsplit("::").next().unwrap_or(full);
    let debug = format!("{err:?}");
    let variant = debug
        .split(|c: char| !(c.is_alphanumeric() || c == '_'))
        .next()
        .unwrap_or_default();
    if variant.is_empty() || variant == short {
        short.to_string()
    } else {
        format!("{short}::{variant}")
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[derive(Debug, thiserror::Error)]
    enum SampleError {
        #[error("cart is empty")]
        EmptyCart { user_id: String },
    }

    #[test]
    fn exception_type_names_the_variant() {
        let err = SampleError::EmptyCart {
            user_id: "user1".into(),
        };
        assert_eq!(exception_type(&err), "SampleError::EmptyCart");
    }

    #[test]
    fn record_exception_without_a_span_does_not_panic() {
        record_exception(&std::io::Error::other("boom"), "boom");
    }
}
