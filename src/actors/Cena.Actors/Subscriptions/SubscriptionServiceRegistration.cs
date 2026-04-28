// =============================================================================
// Cena Platform — SubscriptionServiceRegistration (EPIC-PRR-I, ADR-0057)
//
// DI wiring. Two composition modes:
//   AddSubscriptions(services)       — InMemory store (dev/test default)
//   AddSubscriptionsMarten(services) — Marten-backed store (prod)
//
// Both register the shared services (entitlement resolver, cap enforcer,
// routing policy, payment gateway). The gateway default is SandboxPayment;
// production composition overrides with Stripe/Bit/PayBox adapters.
// =============================================================================

using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Actors.Subscriptions;

/// <summary>DI registration helpers for the Subscriptions bounded context.</summary>
public static class SubscriptionServiceRegistration
{
    /// <summary>
    /// Register with the in-memory store. Suitable for dev/test and
    /// single-instance deployments (matches the ADR-0042 migration pattern
    /// where InMemory is the v1).
    /// </summary>
    public static IServiceCollection AddSubscriptions(this IServiceCollection services)
    {
        services.AddSingleton<ISubscriptionAggregateStore, InMemorySubscriptionAggregateStore>();
        AddSharedServices(services);
        return services;
    }

    /// <summary>
    /// Register with the Marten-backed store and register the Subscriptions
    /// Marten context (event types + entitlement projection) via
    /// <c>ConfigureMarten</c>. Requires <c>AddMarten</c> to have been called
    /// first (composition-root convention).
    /// </summary>
    public static IServiceCollection AddSubscriptionsMarten(this IServiceCollection services)
    {
        services.AddSingleton<ISubscriptionAggregateStore, MartenSubscriptionAggregateStore>();

        // task t_b89826b8bd60 production wiring: replace the InMemory
        // trial-allotment store with the Marten-backed singleton + audit-
        // event store so super-admin changes survive pod restarts and the
        // audit trail is durable.
        services.Replace(ServiceDescriptor.Singleton<ITrialAllotmentConfigStore,
            MartenTrialAllotmentConfigStore>());

        services.ConfigureMarten(opts => opts.RegisterSubscriptionsContext());
        AddSharedServices(services);

        // Per-user discount-codes: replace the InMemory store with the
        // Marten-backed store so discount issuances persist across pod
        // restarts and are visible across replicas (admin issues on pod A,
        // student lookup on pod B finds it).
        services.Replace(ServiceDescriptor.Singleton<IDiscountAssignmentStore,
            MartenDiscountAssignmentStore>());

        // PRR-344 production grace wiring:
        //   - Replace the InMemory seed source with Marten so the admin-
        //     supplied seed list survives pod restarts and is shared
        //     across replicas (the worker on pod B reads what the admin
        //     endpoint on pod A persisted).
        //   - Replace the InMemory grace marker reader with Marten so the
        //     entitlement resolver on any replica sees the same markers
        //     the worker wrote to Postgres.
        //   - Register the worker as a hosted service so it runs on
        //     Actor-Host startup without additional composition work.
        services.Replace(ServiceDescriptor.Singleton<IAlphaMigrationSeedSource,
            MartenAlphaMigrationSeedSource>());
        services.Replace(ServiceDescriptor.Singleton<IAlphaGraceMarkerReader,
            MartenAlphaGraceMarkerReader>());
        // Register once as a concrete singleton so the admin /run-now
        // endpoint can resolve it; register again as an IHostedService so
        // it runs on startup. The same instance is used both ways.
        services.AddSingleton<AlphaUserMigrationWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<AlphaUserMigrationWorker>());
        // PRR-306 production refund-usage probe: aggregates Marten-backed
        // photo usage + raw hint events across linked students.
        services.Replace(ServiceDescriptor.Singleton<IRefundUsageProbe, MartenRefundUsageProbe>());

        // PRR-330 production wiring:
        //   - Replace the InMemory unit-economics snapshot store default
        //     (registered in AddSharedServices) with MartenUnitEconomicsSnapshotStore
        //     so weekly rollups survive pod restarts and are shared across replicas.
        //   - Register UnitEconomicsRollupWorker as a hosted service so Marten-
        //     mode hosts (Actor Host in production) fire the weekly Sunday
        //     06:00 UTC rollup. Non-Marten hosts (tests, dev single-host)
        //     keep the InMemory store without the hosted worker running.
        services.Replace(ServiceDescriptor.Singleton<
            IUnitEconomicsSnapshotStore, MartenUnitEconomicsSnapshotStore>());
        // AddOptions<>().BindConfiguration(...) is the modern options-pattern
        // wiring; we TryAdd the bare Configure here so a host that already
        // bound the section (with custom post-processing) is not clobbered.
        // Hosts can override via
        //   services.Configure<UnitEconomicsRollupOptions>(
        //     configuration.GetSection(UnitEconomicsRollupOptions.SectionName));
        // in Program.cs; the defaults baked into the POCO give a safe
        // no-config boot.
        services.AddOptions<UnitEconomicsRollupOptions>();
        services.AddHostedService<UnitEconomicsRollupWorker>();

        // PRR-345 subscription lifecycle email worker — scans the Marten
        // event log for lifecycle triggers and dispatches via
        // ISubscriptionLifecycleEmailDispatcher. Requires IDocumentStore,
        // so it only registers in Marten-mode hosts (Actor-Host production).
        // Non-Marten hosts (tests, dev single-host) can still exercise the
        // pure RunOnceAsync logic directly via unit tests.
        services.AddHostedService<SubscriptionLifecycleEmailWorker>();

        // Phase 1B trial-then-paywall — L3a card-fingerprint ledger.
        // Replace the InMemory default with the Marten-backed store so the
        // ledger row survives pod restarts and is shared across replicas
        // (recording on pod A is visible to the trial-start path on pod B).
        // The GDPR-retention behaviour (row survives crypto-shred, parent
        // ref cleared) is identical between InMemory and Marten variants.
        services.Replace(ServiceDescriptor.Singleton<ITrialFingerprintLedgerStore,
            MartenTrialFingerprintLedgerStore>());
        return services;
    }

    private static void AddSharedServices(IServiceCollection services)
    {
        services.AddSingleton<IStudentEntitlementResolver, StudentEntitlementResolver>();

        // task t_b89826b8bd60 — platform-wide trial-allotment config.
        // InMemory default; AddSubscriptionsMarten swaps for the Marten-
        // backed singleton document + audit-event-stream variant. TryAdd so
        // a host that has already bound a custom impl wins.
        services.TryAddSingleton<ITrialAllotmentConfigStore,
            InMemoryTrialAllotmentConfigStore>();

        // PRR-304 bank-transfer reservation. InMemory default for single-host;
        // Marten variant is a follow-up when multi-replica parents need a
        // shared view of reservations. The service is Singleton (stateless
        // orchestrator) so the same instance the Student-side endpoint calls
        // is the one the Admin-side confirm endpoint calls and the one the
        // daily expiry worker calls — no split-brain across reservations.
        services.TryAddSingleton<IBankTransferReservationStore,
            InMemoryBankTransferReservationStore>();
        services.AddSingleton<BankTransferReservationService>();
        services.AddOptions<BankTransferExpiryWorkerOptions>();
        services.AddHostedService<BankTransferExpiryWorker>();

        // PRR-345 subscription lifecycle emails (welcome / renewal-upcoming /
        // past-due / cancellation / refund). The worker is registered by
        // AddSubscriptionsMarten below because it needs Marten's
        // IDocumentStore for event scanning; the dispatcher seam defaults
        // to the NullDispatcher (dev-safe no-op). Hosts bind their concrete
        // dispatcher (SMTP / SES / Mailgun with HE/AR/EN templates) by
        // replacing the Null binding; TryAdd below makes that trivial.
        services.TryAddSingleton<ISubscriptionLifecycleEmailDispatcher,
            NullSubscriptionLifecycleEmailDispatcher>();
        services.AddOptions<SubscriptionLifecycleEmailWorkerOptions>();

        // Per-user discount-codes: store, coupon provider, dispatcher, service.
        //   Default store      = InMemory (single-host); AddSubscriptionsMarten
        //                        replaces with Marten-backed.
        //   Default coupon prv = InMemory (StripeServiceRegistration replaces
        //                        with StripeDiscountCouponProvider when Stripe
        //                        is configured).
        //   Default dispatcher = Null (host wires concrete impl with localized
        //                        templates).
        //   Service is Singleton (stateless orchestrator).
        services.TryAddSingleton<IDiscountAssignmentStore, InMemoryDiscountAssignmentStore>();
        services.TryAddSingleton<IDiscountCouponProvider, InMemoryDiscountCouponProvider>();
        services.TryAddSingleton<IDiscountIssuedEmailDispatcher, NullDiscountIssuedEmailDispatcher>();
        services.AddSingleton<DiscountAssignmentService>();

        // Phase 1B trial-then-paywall — L3a card-fingerprint ledger.
        //   Default store = InMemory (production-grade for single-host).
        //   AddSubscriptionsMarten replaces with MartenTrialFingerprintLedgerStore
        //   so multi-replica deployments share one canonical ledger.
        // TryAdd so a host that has already bound a custom impl wins.
        services.TryAddSingleton<ITrialFingerprintLedgerStore,
            InMemoryTrialFingerprintLedgerStore>();

        // PRR-344 alpha-migration defaults (InMemory). AddSubscriptionsMarten
        // swaps these for the Marten-backed variants so the seed list and
        // grace markers persist across pod restarts. TryAdd so a host that
        // has already bound custom impls wins.
        services.TryAddSingleton<IAlphaMigrationSeedSource,
            InMemoryAlphaMigrationSeedSource>();
        services.TryAddSingleton<IAlphaGraceMarkerReader,
            InMemoryAlphaGraceMarkerReader>();
        services.AddSingleton<IPerTierCapEnforcer, PerTierCapEnforcer>();
        services.AddSingleton<ITierLlmRoutingPolicy, TierLlmRoutingPolicy>();
        // Default gateway = sandbox. Production composition root overrides
        // by calling AddSingleton<IPaymentGateway, StripePaymentGateway>()
        // AFTER AddSubscriptions (last registration wins on resolve).
        if (!services.Any(d => d.ServiceType == typeof(IPaymentGateway)))
        {
            services.AddSingleton<IPaymentGateway, SandboxPaymentGateway>();
        }
        // Checkout-session provider: sandbox by default. StripeServiceRegistration
        // .AddStripeCheckoutIfConfigured() replaces it when Stripe config is present.
        if (!services.Any(d => d.ServiceType == typeof(ICheckoutSessionProvider)))
        {
            services.AddSingleton<ICheckoutSessionProvider, SandboxCheckoutSessionProvider>();
        }

        // Phase 1C trial-then-paywall §4.0: payment-method tokenization seam.
        // InMemory default — deterministic SHA256 fingerprints, all five
        // §4.0.1 failure modes exercisable via the scenario factory.
        // StripeServiceRegistration.AddStripeCheckoutIfConfigured replaces
        // this with StripeSetupIntentProvider when both StripeOptions is
        // fully configured AND StripeOptions.EnableSetupIntents is true.
        if (!services.Any(d => d.ServiceType == typeof(IPaymentMethodSetupProvider)))
        {
            services.AddSingleton<IPaymentMethodSetupProvider>(
                _ => new InMemorySetupIntentProvider());
        }

        // PRR-306 refund workflow composition:
        //   - Default refund gateway = sandbox. StripeServiceRegistration
        //     replaces this with StripeRefundGatewayService when Stripe
        //     config is present.
        //   - Usage probe default = noop (policy denies only on explicit
        //     abuse signals; production replaces with MartenRefundUsageProbe).
        //   - Original charge lookup walks the event stream so any host
        //     with ISubscriptionAggregateStore wired gets correct behaviour.
        //   - RefundService is the orchestrator; endpoints resolve it.
        if (!services.Any(d => d.ServiceType == typeof(IRefundGatewayService)))
        {
            services.AddSingleton<IRefundGatewayService, SandboxRefundGatewayService>();
        }
        if (!services.Any(d => d.ServiceType == typeof(IRefundUsageProbe)))
        {
            services.AddSingleton<IRefundUsageProbe, NoopRefundUsageProbe>();
        }
        if (!services.Any(d => d.ServiceType == typeof(IOriginalChargeLookup)))
        {
            services.AddSingleton<IOriginalChargeLookup, EventStreamOriginalChargeLookup>();
        }
        services.AddSingleton<RefundService>();

        // PRR-331: churn-reason capture. In-memory default (test fixture);
        // Marten-backed variant is a follow-up task when we need cross-host
        // aggregation. TryAdd so a host that already bound a Marten impl
        // wins.
        if (!services.Any(d => d.ServiceType == typeof(IChurnReasonRepository)))
        {
            services.AddSingleton<IChurnReasonRepository, InMemoryChurnReasonRepository>();
        }

        // PRR-330: weekly unit-economics snapshot store. InMemory default is
        // production-grade for single-host installs; AddSubscriptionsMarten
        // replaces it with MartenUnitEconomicsSnapshotStore. The aggregation
        // service itself depends on IDocumentStore and is registered here
        // so the Marten-mode admin endpoint and the rollup worker both
        // resolve the same instance.
        if (!services.Any(d => d.ServiceType == typeof(IUnitEconomicsSnapshotStore)))
        {
            services.AddSingleton<IUnitEconomicsSnapshotStore, InMemoryUnitEconomicsSnapshotStore>();
        }
        if (!services.Any(d => d.ServiceType == typeof(UnitEconomicsAggregationService)))
        {
            services.AddSingleton<UnitEconomicsAggregationService>();
        }
        if (!services.Any(d => d.ServiceType == typeof(IUnitEconomicsAggregationService)))
        {
            services.AddSingleton<IUnitEconomicsAggregationService>(
                sp => sp.GetRequiredService<UnitEconomicsAggregationService>());
        }
    }
}
