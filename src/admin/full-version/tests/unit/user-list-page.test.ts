/**
 * Tests for the User List page template and logic.
 *
 * Validates:
 *  - Page uses correct CASL meta for Users read
 *  - Role filter options match CenaRole enum
 *  - Status filter options match system statuses
 *  - Table headers are correctly defined
 *  - Tenant-scoped API call includes school filter
 *  - User actions (suspend, activate, delete) are present
 *
 * FIND-qa-008: baseline admin test infrastructure
 */
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const userListPage = resolve(__dirname, '../../src/pages/apps/user/list/index.vue')
const content = readFileSync(userListPage, 'utf-8')

describe('User List page structure', () => {
  it('declares CASL meta for read:Users', () => {
    expect(content).toContain("action: 'read'")
    expect(content).toContain("subject: 'Users'")
  })

  it('fetches users from /admin/users endpoint', () => {
    expect(content).toContain("'/admin/users'")
  })

  it('includes school filter for tenant scoping', () => {
    expect(content).toContain('selectedSchool')
    expect(content).toContain('school: selectedSchool')
  })

  it('defines all 6 CenaRole options in the role filter', () => {
    const requiredRoles = ['STUDENT', 'TEACHER', 'PARENT', 'MODERATOR', 'ADMIN', 'SUPER_ADMIN']
    for (const role of requiredRoles) {
      expect(content).toContain(`'${role}'`)
    }
  })

  it('defines correct status options', () => {
    expect(content).toContain("value: 'active'")
    expect(content).toContain("value: 'suspended'")
    expect(content).toContain("value: 'pending'")
  })

  it('has correct table headers', () => {
    const expectedHeaders = ['User', 'Role', 'Status', 'School', 'Grade', 'Created', 'Actions']
    for (const header of expectedHeaders) {
      expect(content).toContain(`title: '${header}'`)
    }
  })

  it('has suspend user action', () => {
    expect(content).toContain('suspendUser')
    expect(content).toContain('/suspend')
  })

  it('has activate user action', () => {
    expect(content).toContain('activateUser')
    expect(content).toContain('/activate')
  })

  it('has delete user action', () => {
    expect(content).toContain('deleteUser')
    expect(content).toContain("method: 'DELETE'")
  })

  it('has Add New User button', () => {
    expect(content).toContain('Add New User')
    expect(content).toContain('AddNewUserDrawer')
  })

  it('has Export CSV functionality', () => {
    expect(content).toContain('exportCsv')
    expect(content).toContain('text/csv')
  })

  it('fetches widget stats from /admin/users/stats', () => {
    expect(content).toContain("'/admin/users/stats'")
  })
})
