"""
Cena Platform — LLM Cost Tracking & Budget Enforcement
Layer: LLM ACL | Runtime: Python 3.12+ / FastAPI

Tracks per-student, per-model, and per-task token usage and costs.
Enforces daily budget hard cutoffs (25K output tokens/day).
Generates alerts and monthly projections.
"""

from __future__ import annotations

import hashlib
import logging
from collections import defaultdict
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from enum import StrEnum
from typing import Protocol

logger = logging.getLogger("cena.llm.cost_tracking")


# ═══════════════════════════════════════════════════════════════════════
# 1. PRICING TABLE (from routing-config.yaml, verified March 2026)
# ═══════════════════════════════════════════════════════════════════════

@dataclass(frozen=True)
class ModelPricing:
    """Immutable pricing for a single model.

    All costs are in USD per million tokens (MTok).
    """
    model_id: str
    input_cost_per_mtok: float
    output_cost_per_mtok: float
    cached_input_cost_per_mtok: float

    def calculate_cost(
        self,
        input_tokens: int,
        output_tokens: int,
        cached_input_tokens: int = 0,
    ) -> float:
        """Calculate total cost for a single LLM invocation.

        Args:
            input_tokens: Non-cached input token count.
            output_tokens: Output token count.
            cached_input_tokens: Cached input token count (cheaper rate).

        Returns:
            Total cost in USD.
        """
        fresh_input_cost = (input_tokens / 1_000_000) * self.input_cost_per_mtok
        cached_input_cost = (cached_input_tokens / 1_000_000) * self.cached_input_cost_per_mtok
        output_cost = (output_tokens / 1_000_000) * self.output_cost_per_mtok
        return fresh_input_cost + cached_input_cost + output_cost


# Pricing table — update when models change. Sourced from routing-config.yaml.
PRICING: dict[str, ModelPricing] = {
    "kimi-k2-turbo": ModelPricing(
        model_id="kimi-k2-turbo",
        input_cost_per_mtok=0.40,
        output_cost_per_mtok=2.00,
        cached_input_cost_per_mtok=0.10,
    ),
    "kimi-k2-0905-preview": ModelPricing(
        model_id="kimi-k2-0905-preview",
        input_cost_per_mtok=0.40,
        output_cost_per_mtok=2.00,
        cached_input_cost_per_mtok=0.10,
    ),
    "kimi-k2.5": ModelPricing(
        model_id="kimi-k2.5",
        input_cost_per_mtok=0.45,
        output_cost_per_mtok=2.20,
        cached_input_cost_per_mtok=0.12,
    ),
    "claude-sonnet-4-6-20260215": ModelPricing(
        model_id="claude-sonnet-4-6-20260215",
        input_cost_per_mtok=3.00,
        output_cost_per_mtok=15.00,
        cached_input_cost_per_mtok=0.30,
    ),
    "claude-sonnet-4-5-20260101": ModelPricing(
        model_id="claude-sonnet-4-5-20260101",
        input_cost_per_mtok=3.00,
        output_cost_per_mtok=15.00,
        cached_input_cost_per_mtok=0.30,
    ),
    "claude-haiku-4-5-20260101": ModelPricing(
        model_id="claude-haiku-4-5-20260101",
        input_cost_per_mtok=1.00,
        output_cost_per_mtok=5.00,
        cached_input_cost_per_mtok=0.10,
    ),
    "claude-opus-4-6-20260215": ModelPricing(
        model_id="claude-opus-4-6-20260215",
        input_cost_per_mtok=5.00,
        output_cost_per_mtok=25.00,
        cached_input_cost_per_mtok=0.50,
    ),
}


# ═══════════════════════════════════════════════════════════════════════
# 2. TOKEN COUNTER
# ═══════════════════════════════════════════════════════════════════════

class AlertSeverity(StrEnum):
    INFO = "info"
    WARNING = "warning"
    CRITICAL = "critical"


@dataclass
class TokenUsage:
    """Token usage for a single LLM invocation."""
    input_tokens: int
    output_tokens: int
    cached_input_tokens: int = 0
    model_id: str = ""
    cost_usd: float = 0.0

    @property
    def total_tokens(self) -> int:
        return self.input_tokens + self.output_tokens + self.cached_input_tokens


class AlertCallback(Protocol):
    """Protocol for alert delivery. Implement to send to Slack, PagerDuty, etc."""
    def __call__(
        self,
        severity: AlertSeverity,
        message: str,
        context: dict,
    ) -> None: ...


@dataclass
class TokenCounter:
    """Tracks cumulative token usage per model.

    Thread-safe design: in production, back with Redis or PostgreSQL.
    This in-memory implementation is for contract definition and testing.
    """
    _usage_by_model: dict[str, TokenUsage] = field(default_factory=lambda: defaultdict(
        lambda: TokenUsage(input_tokens=0, output_tokens=0, cached_input_tokens=0)
    ))
    _total: TokenUsage = field(default_factory=lambda: TokenUsage(input_tokens=0, output_tokens=0))

    def record(
        self,
        model_id: str,
        input_tokens: int,
        output_tokens: int,
        cached_input_tokens: int = 0,
    ) -> TokenUsage:
        """Record token usage for an invocation and return the usage with cost.

        Args:
            model_id: The model that was called.
            input_tokens: Fresh (non-cached) input tokens.
            output_tokens: Output tokens generated.
            cached_input_tokens: Input tokens served from cache.

        Returns:
            TokenUsage with computed cost.
        """
        pricing = PRICING.get(model_id)
        cost = pricing.calculate_cost(input_tokens, output_tokens, cached_input_tokens) if pricing else 0.0

        usage = TokenUsage(
            input_tokens=input_tokens,
            output_tokens=output_tokens,
            cached_input_tokens=cached_input_tokens,
            model_id=model_id,
            cost_usd=cost,
        )

        # Accumulate per-model
        model_total = self._usage_by_model[model_id]
        model_total.input_tokens += input_tokens
        model_total.output_tokens += output_tokens
        model_total.cached_input_tokens += cached_input_tokens
        model_total.cost_usd += cost

        # Accumulate global
        self._total.input_tokens += input_tokens
        self._total.output_tokens += output_tokens
        self._total.cached_input_tokens += cached_input_tokens
        self._total.cost_usd += cost

        return usage

    def get_model_total(self, model_id: str) -> TokenUsage:
        """Get cumulative usage for a specific model."""
        return self._usage_by_model.get(model_id, TokenUsage(input_tokens=0, output_tokens=0))

    def get_global_total(self) -> TokenUsage:
        """Get cumulative usage across all models."""
        return self._total

    @property
    def cache_hit_rate(self) -> float:
        """Proportion of input tokens served from cache."""
        total_input = self._total.input_tokens + self._total.cached_input_tokens
        if total_input == 0:
            return 0.0
        return self._total.cached_input_tokens / total_input

    def reset(self) -> None:
        """Reset all counters. Called on daily budget reset."""
        self._usage_by_model.clear()
        self._total = TokenUsage(input_tokens=0, output_tokens=0)


# ═══════════════════════════════════════════════════════════════════════
# 3. DAILY BUDGET
# ═══════════════════════════════════════════════════════════════════════

@dataclass
class DailyBudget:
    """Per-student daily token budget with hard cutoff.

    Default: 25,000 output tokens/day (aligned with routing-config.yaml).
    Hard cutoff prevents runaway costs — students see a friendly "come back tomorrow" message.

    In production, state is persisted in Redis with TTL matching reset_at.
    """
    student_id: str
    daily_output_limit: int = 25_000
    daily_cost_limit_usd: float = 0.70
    daily_cost_hard_limit_usd: float = 1.50
    _used_output_tokens: int = field(default=0, init=False)
    _used_cost_usd: float = field(default=0.0, init=False)
    _reset_at: datetime = field(default_factory=lambda: _next_midnight_israel())
    _alert_callback: AlertCallback | None = field(default=None, repr=False)

    @property
    def used_output_tokens(self) -> int:
        return self._used_output_tokens

    @property
    def remaining_output_tokens(self) -> int:
        return max(0, self.daily_output_limit - self._used_output_tokens)

    @property
    def used_cost_usd(self) -> float:
        return self._used_cost_usd

    @property
    def usage_percentage(self) -> float:
        if self.daily_output_limit == 0:
            return 1.0
        return self._used_output_tokens / self.daily_output_limit

    @property
    def is_exhausted(self) -> bool:
        return self._used_output_tokens >= self.daily_output_limit

    @property
    def is_cost_hard_limit_hit(self) -> bool:
        return self._used_cost_usd >= self.daily_cost_hard_limit_usd

    @property
    def reset_at(self) -> datetime:
        return self._reset_at

    def check_and_reset_if_needed(self) -> bool:
        """Check if the budget period has elapsed and reset if so.

        Returns:
            True if budget was reset, False otherwise.
        """
        now = datetime.now(timezone.utc)
        if now >= self._reset_at:
            self._used_output_tokens = 0
            self._used_cost_usd = 0.0
            self._reset_at = _next_midnight_israel()
            logger.info(
                "Daily budget reset for student %s. New reset at %s",
                _hash_student_id(self.student_id),
                self._reset_at.isoformat(),
            )
            return True
        return False

    def can_afford(self, estimated_output_tokens: int) -> bool:
        """Check if the student can afford the estimated output tokens.

        Args:
            estimated_output_tokens: Expected output tokens for the next request.

        Returns:
            True if within budget, False if would exceed.
        """
        self.check_and_reset_if_needed()
        return (self._used_output_tokens + estimated_output_tokens) <= self.daily_output_limit

    def consume(self, output_tokens: int, cost_usd: float) -> None:
        """Record token consumption and trigger alerts if thresholds are crossed.

        Args:
            output_tokens: Actual output tokens consumed.
            cost_usd: Actual cost in USD.

        Raises:
            BudgetExhaustedError: If consumption would exceed hard limit.
                Imported from acl_interfaces at runtime to avoid circular imports.
        """
        self.check_and_reset_if_needed()

        self._used_output_tokens += output_tokens
        self._used_cost_usd += cost_usd

        # Check alert thresholds
        pct = self.usage_percentage

        if pct >= 1.50:
            self._fire_alert(
                AlertSeverity.CRITICAL,
                f"Student {_hash_student_id(self.student_id)} at {pct:.0%} of daily budget "
                f"({self._used_output_tokens}/{self.daily_output_limit} output tokens, "
                f"${self._used_cost_usd:.4f} cost)",
            )
        elif pct >= 0.80:
            self._fire_alert(
                AlertSeverity.WARNING,
                f"Student {_hash_student_id(self.student_id)} at {pct:.0%} of daily budget",
            )

        if self._used_cost_usd >= self.daily_cost_limit_usd:
            self._fire_alert(
                AlertSeverity.WARNING,
                f"Student {_hash_student_id(self.student_id)} cost ${self._used_cost_usd:.4f} "
                f"exceeds soft limit ${self.daily_cost_limit_usd:.2f}",
            )

    def set_alert_callback(self, callback: AlertCallback) -> None:
        """Register an alert callback for budget threshold notifications."""
        self._alert_callback = callback

    def _fire_alert(self, severity: AlertSeverity, message: str) -> None:
        logger.log(
            logging.CRITICAL if severity == AlertSeverity.CRITICAL else logging.WARNING,
            message,
        )
        if self._alert_callback:
            self._alert_callback(
                severity=severity,
                message=message,
                context={
                    "student_id_hash": _hash_student_id(self.student_id),
                    "used_output_tokens": self._used_output_tokens,
                    "daily_limit": self.daily_output_limit,
                    "used_cost_usd": self._used_cost_usd,
                    "reset_at": self._reset_at.isoformat(),
                },
            )

    def to_dict(self) -> dict:
        """Serialize budget state for API responses and persistence."""
        return {
            "student_id_hash": _hash_student_id(self.student_id),
            "daily_output_limit": self.daily_output_limit,
            "used_output_tokens": self._used_output_tokens,
            "remaining_output_tokens": self.remaining_output_tokens,
            "usage_percentage": round(self.usage_percentage, 4),
            "used_cost_usd": round(self._used_cost_usd, 6),
            "is_exhausted": self.is_exhausted,
            "reset_at": self._reset_at.isoformat(),
        }


# ═══════════════════════════════════════════════════════════════════════
# 4. COST AGGREGATOR
# ═══════════════════════════════════════════════════════════════════════

@dataclass
class CostRecord:
    """Immutable record of a single LLM invocation cost."""
    request_id: str
    student_id_hash: str
    task_type: str
    model_id: str
    input_tokens: int
    output_tokens: int
    cached_input_tokens: int
    cost_usd: float
    latency_ms: int
    timestamp: datetime = field(default_factory=lambda: datetime.now(timezone.utc))


@dataclass
class AggregatedCost:
    """Summary of costs for one dimension slice (student, model, or task)."""
    dimension: str         # "student", "model", or "task"
    dimension_value: str   # The specific student hash, model_id, or task_type
    total_requests: int = 0
    total_input_tokens: int = 0
    total_output_tokens: int = 0
    total_cached_input_tokens: int = 0
    total_cost_usd: float = 0.0
    total_latency_ms: int = 0

    @property
    def avg_cost_per_request(self) -> float:
        return self.total_cost_usd / self.total_requests if self.total_requests > 0 else 0.0

    @property
    def avg_latency_ms(self) -> float:
        return self.total_latency_ms / self.total_requests if self.total_requests > 0 else 0.0

    @property
    def cache_hit_rate(self) -> float:
        total_input = self.total_input_tokens + self.total_cached_input_tokens
        return self.total_cached_input_tokens / total_input if total_input > 0 else 0.0


class CostAggregator:
    """Aggregates cost records across multiple dimensions.

    Provides per-student, per-task, and per-model rollups.
    In production, this is backed by a time-series database (e.g., TimescaleDB)
    or a pre-aggregated materialized view.

    This in-memory implementation serves as the contract + test harness.
    """

    def __init__(self, alert_callback: AlertCallback | None = None) -> None:
        self._records: list[CostRecord] = []
        self._by_student: dict[str, AggregatedCost] = defaultdict(
            lambda: AggregatedCost(dimension="student", dimension_value="")
        )
        self._by_model: dict[str, AggregatedCost] = defaultdict(
            lambda: AggregatedCost(dimension="model", dimension_value="")
        )
        self._by_task: dict[str, AggregatedCost] = defaultdict(
            lambda: AggregatedCost(dimension="task", dimension_value="")
        )
        self._daily_cost_history: list[tuple[datetime, float]] = []
        self._alert_callback = alert_callback

    def record(
        self,
        request_id: str,
        student_id: str,
        task_type: str,
        model_id: str,
        input_tokens: int,
        output_tokens: int,
        cached_input_tokens: int,
        latency_ms: int,
    ) -> CostRecord:
        """Record a completed LLM invocation.

        Computes cost from the pricing table, updates all aggregation dimensions,
        and checks for cost spike alerts.

        Args:
            request_id: Unique request identifier.
            student_id: Raw student ID (hashed before storage).
            task_type: Task type string (from TaskType enum).
            model_id: Provider model ID.
            input_tokens: Fresh input tokens.
            output_tokens: Output tokens.
            cached_input_tokens: Cached input tokens.
            latency_ms: End-to-end latency in milliseconds.

        Returns:
            The CostRecord created.
        """
        pricing = PRICING.get(model_id)
        cost = pricing.calculate_cost(input_tokens, output_tokens, cached_input_tokens) if pricing else 0.0

        student_hash = _hash_student_id(student_id)

        record = CostRecord(
            request_id=request_id,
            student_id_hash=student_hash,
            task_type=task_type,
            model_id=model_id,
            input_tokens=input_tokens,
            output_tokens=output_tokens,
            cached_input_tokens=cached_input_tokens,
            cost_usd=cost,
            latency_ms=latency_ms,
        )
        self._records.append(record)

        # Update aggregations
        self._update_aggregation(self._by_student, student_hash, "student", record)
        self._update_aggregation(self._by_model, model_id, "model", record)
        self._update_aggregation(self._by_task, task_type, "task", record)

        return record

    def get_student_summary(self, student_id: str) -> AggregatedCost:
        """Get cost summary for a specific student."""
        return self._by_student.get(
            _hash_student_id(student_id),
            AggregatedCost(dimension="student", dimension_value=_hash_student_id(student_id)),
        )

    def get_model_summary(self, model_id: str) -> AggregatedCost:
        """Get cost summary for a specific model."""
        return self._by_model.get(
            model_id,
            AggregatedCost(dimension="model", dimension_value=model_id),
        )

    def get_task_summary(self, task_type: str) -> AggregatedCost:
        """Get cost summary for a specific task type."""
        return self._by_task.get(
            task_type,
            AggregatedCost(dimension="task", dimension_value=task_type),
        )

    def get_all_student_summaries(self) -> dict[str, AggregatedCost]:
        """Get cost summaries for all students."""
        return dict(self._by_student)

    def get_all_model_summaries(self) -> dict[str, AggregatedCost]:
        """Get cost summaries for all models."""
        return dict(self._by_model)

    def get_all_task_summaries(self) -> dict[str, AggregatedCost]:
        """Get cost summaries for all task types."""
        return dict(self._by_task)

    def get_total_cost(self) -> float:
        """Get total cost across all records."""
        return sum(r.cost_usd for r in self._records)

    def get_total_requests(self) -> int:
        """Get total number of recorded requests."""
        return len(self._records)

    # ── Monthly Projection ──

    def project_monthly_cost(self, active_student_count: int) -> MonthlyProjection:
        """Project monthly cost based on current burn rate.

        Uses the trailing 7-day average daily cost per student, extrapolated to 30 days.

        Args:
            active_student_count: Number of currently active students.

        Returns:
            MonthlyProjection with estimated costs and confidence interval.
        """
        now = datetime.now(timezone.utc)
        seven_days_ago = now - timedelta(days=7)

        recent_records = [r for r in self._records if r.timestamp >= seven_days_ago]
        if not recent_records:
            return MonthlyProjection(
                projected_monthly_total_usd=0.0,
                projected_per_student_usd=0.0,
                confidence="low",
                daily_burn_rate_usd=0.0,
                active_students=active_student_count,
                data_days=0,
            )

        # Calculate daily averages
        daily_costs: dict[str, float] = defaultdict(float)
        for r in recent_records:
            day_key = r.timestamp.strftime("%Y-%m-%d")
            daily_costs[day_key] += r.cost_usd

        data_days = len(daily_costs)
        total_recent_cost = sum(daily_costs.values())
        avg_daily_cost = total_recent_cost / data_days if data_days > 0 else 0.0

        projected_monthly = avg_daily_cost * 30
        projected_per_student = projected_monthly / active_student_count if active_student_count > 0 else 0.0

        # Confidence based on data quantity
        confidence: str
        if data_days >= 7:
            confidence = "high"
        elif data_days >= 3:
            confidence = "medium"
        else:
            confidence = "low"

        return MonthlyProjection(
            projected_monthly_total_usd=round(projected_monthly, 2),
            projected_per_student_usd=round(projected_per_student, 4),
            confidence=confidence,
            daily_burn_rate_usd=round(avg_daily_cost, 4),
            active_students=active_student_count,
            data_days=data_days,
        )

    # ── Cost Spike Detection ──

    def check_cost_spike(
        self,
        student_id: str,
        spike_multiplier: float = 2.0,
    ) -> CostSpikeAlert | None:
        """Check if a student's current daily cost exceeds their rolling average.

        Args:
            student_id: Raw student ID.
            spike_multiplier: Alert if today's cost > multiplier * 7-day average.

        Returns:
            CostSpikeAlert if spike detected, None otherwise.
        """
        student_hash = _hash_student_id(student_id)
        now = datetime.now(timezone.utc)
        today_start = now.replace(hour=0, minute=0, second=0, microsecond=0)
        seven_days_ago = today_start - timedelta(days=7)

        student_records = [r for r in self._records if r.student_id_hash == student_hash]
        today_cost = sum(r.cost_usd for r in student_records if r.timestamp >= today_start)
        week_records = [r for r in student_records if seven_days_ago <= r.timestamp < today_start]

        if not week_records:
            return None

        daily_costs: dict[str, float] = defaultdict(float)
        for r in week_records:
            day_key = r.timestamp.strftime("%Y-%m-%d")
            daily_costs[day_key] += r.cost_usd

        avg_daily = sum(daily_costs.values()) / len(daily_costs) if daily_costs else 0.0
        threshold = avg_daily * spike_multiplier

        if today_cost > threshold and threshold > 0:
            alert = CostSpikeAlert(
                student_id_hash=student_hash,
                today_cost_usd=round(today_cost, 6),
                rolling_avg_daily_usd=round(avg_daily, 6),
                spike_multiplier=spike_multiplier,
                threshold_usd=round(threshold, 6),
            )
            if self._alert_callback:
                self._alert_callback(
                    severity=AlertSeverity.WARNING,
                    message=f"Cost spike detected for student {student_hash}: "
                            f"${today_cost:.4f} today vs ${avg_daily:.4f} avg "
                            f"({spike_multiplier}x threshold)",
                    context={
                        "student_id_hash": student_hash,
                        "today_cost": today_cost,
                        "rolling_avg": avg_daily,
                        "threshold": threshold,
                    },
                )
            return alert

        return None

    def _update_aggregation(
        self,
        agg_dict: dict[str, AggregatedCost],
        key: str,
        dimension: str,
        record: CostRecord,
    ) -> None:
        if key not in agg_dict:
            agg_dict[key] = AggregatedCost(dimension=dimension, dimension_value=key)
        agg = agg_dict[key]
        agg.total_requests += 1
        agg.total_input_tokens += record.input_tokens
        agg.total_output_tokens += record.output_tokens
        agg.total_cached_input_tokens += record.cached_input_tokens
        agg.total_cost_usd += record.cost_usd
        agg.total_latency_ms += record.latency_ms


# ═══════════════════════════════════════════════════════════════════════
# 5. PROJECTION & ALERT MODELS
# ═══════════════════════════════════════════════════════════════════════

@dataclass(frozen=True)
class MonthlyProjection:
    """Monthly cost projection based on current burn rate."""
    projected_monthly_total_usd: float
    projected_per_student_usd: float
    confidence: str                    # "low", "medium", "high"
    daily_burn_rate_usd: float
    active_students: int
    data_days: int                     # Number of days of data used for projection

    def to_dict(self) -> dict:
        return {
            "projected_monthly_total_usd": self.projected_monthly_total_usd,
            "projected_per_student_usd": self.projected_per_student_usd,
            "confidence": self.confidence,
            "daily_burn_rate_usd": self.daily_burn_rate_usd,
            "active_students": self.active_students,
            "data_days": self.data_days,
        }


@dataclass(frozen=True)
class CostSpikeAlert:
    """Alert raised when a student's daily cost exceeds the rolling average threshold."""
    student_id_hash: str
    today_cost_usd: float
    rolling_avg_daily_usd: float
    spike_multiplier: float
    threshold_usd: float

    def to_dict(self) -> dict:
        return {
            "student_id_hash": self.student_id_hash,
            "today_cost_usd": self.today_cost_usd,
            "rolling_avg_daily_usd": self.rolling_avg_daily_usd,
            "spike_multiplier": self.spike_multiplier,
            "threshold_usd": self.threshold_usd,
        }


# ═══════════════════════════════════════════════════════════════════════
# 6. BUDGET MANAGER (orchestrates per-student budgets)
# ═══════════════════════════════════════════════════════════════════════

class BudgetManager:
    """Manages per-student DailyBudget instances.

    In production, budget state is stored in Redis with TTL-based auto-reset.
    This in-memory implementation serves as the contract.
    """

    def __init__(
        self,
        default_daily_output_limit: int = 25_000,
        default_daily_cost_limit_usd: float = 0.70,
        default_daily_cost_hard_limit_usd: float = 1.50,
        alert_callback: AlertCallback | None = None,
    ) -> None:
        self._budgets: dict[str, DailyBudget] = {}
        self._default_daily_output_limit = default_daily_output_limit
        self._default_daily_cost_limit_usd = default_daily_cost_limit_usd
        self._default_daily_cost_hard_limit_usd = default_daily_cost_hard_limit_usd
        self._alert_callback = alert_callback

    def get_budget(self, student_id: str) -> DailyBudget:
        """Get or create a DailyBudget for a student.

        Args:
            student_id: Raw student UUID.

        Returns:
            The student's DailyBudget instance.
        """
        if student_id not in self._budgets:
            budget = DailyBudget(
                student_id=student_id,
                daily_output_limit=self._default_daily_output_limit,
                daily_cost_limit_usd=self._default_daily_cost_limit_usd,
                daily_cost_hard_limit_usd=self._default_daily_cost_hard_limit_usd,
            )
            if self._alert_callback:
                budget.set_alert_callback(self._alert_callback)
            self._budgets[student_id] = budget
        return self._budgets[student_id]

    def can_afford(self, student_id: str, estimated_output_tokens: int) -> bool:
        """Check if a student can afford the estimated tokens."""
        return self.get_budget(student_id).can_afford(estimated_output_tokens)

    def consume(self, student_id: str, output_tokens: int, cost_usd: float) -> None:
        """Record consumption for a student."""
        self.get_budget(student_id).consume(output_tokens, cost_usd)

    def get_all_budgets(self) -> dict[str, dict]:
        """Get serialized budget state for all tracked students."""
        return {sid: b.to_dict() for sid, b in self._budgets.items()}

    def get_exhausted_students(self) -> list[str]:
        """Return list of student IDs whose budgets are exhausted."""
        return [sid for sid, b in self._budgets.items() if b.is_exhausted]


# ═══════════════════════════════════════════════════════════════════════
# 7. UTILITY FUNCTIONS
# ═══════════════════════════════════════════════════════════════════════

def _hash_student_id(student_id: str) -> str:
    """Hash a student ID for logging and storage.

    Uses SHA-256 truncated to 12 hex chars. Deterministic for consistent
    aggregation, but not reversible.
    """
    return hashlib.sha256(student_id.encode()).hexdigest()[:12]


def _next_midnight_israel() -> datetime:
    """Calculate next midnight in Israel Standard Time (UTC+2 / UTC+3 DST).

    Uses UTC+2 as a conservative default. In production, use a proper
    timezone library (zoneinfo) for DST handling.
    """
    now = datetime.now(timezone.utc)
    israel_offset = timedelta(hours=2)
    israel_now = now + israel_offset
    israel_midnight = israel_now.replace(hour=0, minute=0, second=0, microsecond=0) + timedelta(days=1)
    utc_midnight = israel_midnight - israel_offset
    return utc_midnight.replace(tzinfo=timezone.utc)
