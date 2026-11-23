"""Stamp the caller's user ID on every span a request produces."""

from __future__ import annotations

from collections.abc import Iterator
from contextlib import contextmanager

USER_ID_ATTRIBUTE = "user.id"

try:
    from opentelemetry import baggage, context, trace
    from opentelemetry.sdk.trace import SpanProcessor
except ImportError:  # OTel ships with the runtime image only; local dev works without it
    trace = None

if trace is not None:

    class _UserIdSpanProcessor(SpanProcessor):
        # LLM spans carry the token counts, so the ID has to land on them, not just the request span.
        def on_start(self, span, parent_context=None):  # noqa: ANN001
            user_id = baggage.get_baggage(USER_ID_ATTRIBUTE, parent_context)
            if user_id:
                span.set_attribute(USER_ID_ATTRIBUTE, str(user_id))


_installed = False


def _install_processor() -> None:
    global _installed
    if _installed or trace is None:
        return
    provider = trace.get_tracer_provider()
    if hasattr(provider, "add_span_processor"):
        provider.add_span_processor(_UserIdSpanProcessor())
        _installed = True


@contextmanager
def user_context(user_id: str | None) -> Iterator[None]:
    """Tag the current span and every span started inside the block with ``user.id``."""
    if trace is None or not user_id:
        yield
        return
    _install_processor()
    trace.get_current_span().set_attribute(USER_ID_ATTRIBUTE, user_id)
    token = context.attach(baggage.set_baggage(USER_ID_ATTRIBUTE, user_id))
    try:
        yield
    finally:
        context.detach(token)
