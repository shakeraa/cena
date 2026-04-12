import { isToday } from './helpers'
import { formatDateWithLocale, formatNumberWithLocale, toBcp47 } from '@/composables/useLocaleFormatters'
import { getActiveLocale } from '@/plugins/i18n/index'

export const avatarText = (value: string) => {
  if (!value)
    return ''
  const nameArray = value.split(' ')

  return nameArray.map(word => word.charAt(0).toUpperCase()).join('')
}

/**
 * FIND-pedagogy-015: Locale-aware number formatter.
 * Uses Intl.NumberFormat with compact notation for large values (>9999)
 * and locale-aware grouping for smaller values.
 * Replaces the old regex-based ',' separator that was hardcoded to en-US.
 */
export const kFormatter = (num: number) => {
  return formatNumberWithLocale(num, getActiveLocale())
}

/**
 * Format and return date in Humanize format
 * Intl docs: https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Intl/DateTimeFormat/format
 * Intl Constructor: https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Intl/DateTimeFormat/DateTimeFormat
 * FIND-pedagogy-015: Now uses the active i18n locale instead of hardcoded 'en-US'.
 * @param {string} value date to format
 * @param {Intl.DateTimeFormatOptions} formatting Intl object to format with
 */
export const formatDate = (value: string, formatting: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric', year: 'numeric' }) => {
  if (!value)
    return value

  return formatDateWithLocale(value, formatting, getActiveLocale())
}

/**
 * Return short human friendly month representation of date
 * Can also convert date to only time if date is of today (Better UX)
 * FIND-pedagogy-015: Now uses the active i18n locale instead of hardcoded 'en-US'.
 * @param {string} value date to format
 * @param {boolean} toTimeForCurrentDay Shall convert to time if day is today/current
 */
export const formatDateToMonthShort = (value: string, toTimeForCurrentDay = true) => {
  const date = new Date(value)
  let formatting: Record<string, string> = { month: 'short', day: 'numeric' }

  if (toTimeForCurrentDay && isToday(date))
    formatting = { hour: 'numeric', minute: 'numeric' }

  const bcp47 = toBcp47(getActiveLocale())

  return new Intl.DateTimeFormat(bcp47, formatting).format(new Date(value))
}

export const prefixWithPlus = (value: number) => value > 0 ? `+${value}` : value
