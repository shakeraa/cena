/**
 * Tests for the AI Settings page.
 *
 * Validates:
 *  - Page uses correct CASL meta for manage:Settings
 *  - Uses $api for fetching and saving
 *  - Has provider configuration interface
 *  - Contains default settings (language, bloom level, grade)
 *
 * FIND-qa-008: baseline admin test infrastructure
 */
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const aiSettingsPage = resolve(__dirname, '../../src/pages/apps/system/ai-settings.vue')
const content = readFileSync(aiSettingsPage, 'utf-8')

describe('AI Settings page structure', () => {
  it('declares CASL meta for manage:Settings', () => {
    expect(content).toContain("action: 'manage'")
    expect(content).toContain("subject: 'Settings'")
  })

  it('uses $api for data fetching', () => {
    expect(content).toContain('$api')
  })

  it('defines ProviderConfig interface', () => {
    expect(content).toContain('ProviderConfig')
    expect(content).toContain('isEnabled')
    expect(content).toContain('hasApiKey')
    expect(content).toContain('modelId')
    expect(content).toContain('temperature')
  })

  it('has default language setting', () => {
    expect(content).toContain('defaultLanguage')
  })

  it('has default Bloom level setting', () => {
    expect(content).toContain('defaultBloomsLevel')
  })

  it('has questionsPerBatch setting', () => {
    expect(content).toContain('questionsPerBatch')
  })

  it('has autoRunQualityGate setting', () => {
    expect(content).toContain('autoRunQualityGate')
  })

  it('has loading and saving states', () => {
    expect(content).toContain('loading')
    expect(content).toContain('saving')
  })

  it('has provider testing functionality', () => {
    expect(content).toContain('testingProvider')
    expect(content).toContain('testResult')
  })
})
