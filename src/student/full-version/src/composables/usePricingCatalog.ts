// =============================================================================
// Cena Platform — usePricingCatalog composable (EPIC-PRR-I PRR-290/291)
//
// Fetches the public retail pricing catalog from GET /api/v1/tiers.
// Anonymous-OK endpoint (no auth required); ETag cached for 5 minutes.
// Returns strongly-typed tier data matching the Cena.Api.Contracts.Subscriptions
// DTO shape.
// =============================================================================

import { computed, ref, onMounted } from 'vue'
import { useApi } from './useApi'

/** Caps for a single tier. null = unlimited. */
export interface UsageCapsDto {
  sonnetEscalationsPerWeek: number | null
  photoDiagnosticsPerMonth: number | null
  photoDiagnosticsHardCapPerMonth: number | null
  hintRequestsPerMonth: number | null
}

/** Feature flags exposed on the pricing card. */
export interface TierFeatureFlagsDto {
  parentDashboard: boolean
  tutorHandoffPdf: boolean
  arabicDashboard: boolean
  prioritySupport: boolean
}

/** One retail tier on the pricing card. Prices are integer agorot (1/100 ILS). */
export interface RetailTierDto {
  tierId: 'Basic' | 'Plus' | 'Premium'
  monthlyPriceAgorot: number
  annualPriceAgorot: number
  monthlyVatAgorot: number
  annualVatAgorot: number
  caps: UsageCapsDto
  features: TierFeatureFlagsDto
}

/** Sibling discount structure. */
export interface SiblingDiscountDto {
  firstSecondSiblingMonthlyAgorot: number
  thirdPlusSiblingMonthlyAgorot: number
}

/** Full pricing-catalog response. */
export interface PricingCatalogResponseDto {
  tiers: RetailTierDto[]
  siblingDiscount: SiblingDiscountDto
  vatBasisPoints: number
}

/**
 * Composable that fetches the retail pricing catalog.
 * Returns a ref to the DTO plus loading/error state.
 */
export function usePricingCatalog() {
  const catalog = ref<PricingCatalogResponseDto | null>(null)
  const loading = ref(true)
  const error = ref<Error | null>(null)

  const load = async () => {
    loading.value = true
    error.value = null
    try {
      const { data, error: fetchError } = await useApi('/v1/tiers')
        .get()
        .json<PricingCatalogResponseDto>()
      if (fetchError.value) {
        error.value = new Error(fetchError.value.message ?? 'Failed to load pricing catalog')
      } else {
        catalog.value = data.value
      }
    } catch (e) {
      error.value = e instanceof Error ? e : new Error(String(e))
    } finally {
      loading.value = false
    }
  }

  onMounted(load)

  /** Agorot → display shekels string, LOCALE-agnostic (numerals stay LTR). */
  const formatPriceAgorot = (agorot: number): string => {
    const shekels = agorot / 100
    return shekels % 1 === 0 ? `₪${shekels.toFixed(0)}` : `₪${shekels.toFixed(2)}`
  }

  const tiers = computed(() => catalog.value?.tiers ?? [])
  const siblingDiscount = computed(() => catalog.value?.siblingDiscount ?? null)

  return {
    catalog,
    tiers,
    siblingDiscount,
    loading,
    error,
    reload: load,
    formatPriceAgorot,
  }
}
