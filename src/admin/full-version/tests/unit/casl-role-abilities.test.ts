/**
 * Tests for CASL role-to-abilities mapping.
 *
 * Validates:
 *  - SUPER_ADMIN gets manage:all
 *  - ADMIN gets full subject list but only read on System/AuditLog
 *  - MODERATOR gets Content/Questions/Messaging manage, restricted rest
 *  - Unknown role gets empty abilities
 *
 * FIND-qa-008: baseline admin test infrastructure
 */
import { describe, it, expect } from 'vitest'
import { mapRoleToAbilities } from '@/plugins/casl/role-abilities'

describe('mapRoleToAbilities', () => {
  it('SUPER_ADMIN gets manage:all', () => {
    const abilities = mapRoleToAbilities('SUPER_ADMIN')

    expect(abilities).toEqual([{ action: 'manage', subject: 'all' }])
  })

  it('ADMIN can manage Users, Content, Questions, and more', () => {
    const abilities = mapRoleToAbilities('ADMIN')

    const subjects = abilities.map(a => a.subject)

    expect(subjects).toContain('Users')
    expect(subjects).toContain('Content')
    expect(subjects).toContain('Questions')
    expect(subjects).toContain('Analytics')
    expect(subjects).toContain('Focus')
    expect(subjects).toContain('Mastery')
    expect(subjects).toContain('Messaging')
    expect(subjects).toContain('Settings')
  })

  it('ADMIN has only read access to System and AuditLog', () => {
    const abilities = mapRoleToAbilities('ADMIN')

    const systemRule = abilities.find(a => a.subject === 'System')
    const auditRule = abilities.find(a => a.subject === 'AuditLog')

    expect(systemRule).toEqual({ action: 'read', subject: 'System' })
    expect(auditRule).toEqual({ action: 'read', subject: 'AuditLog' })
  })

  it('MODERATOR can manage Content, Questions, Messaging', () => {
    const abilities = mapRoleToAbilities('MODERATOR')

    const manageSubjects = abilities
      .filter(a => a.action === 'manage')
      .map(a => a.subject)

    expect(manageSubjects).toContain('Content')
    expect(manageSubjects).toContain('Questions')
    expect(manageSubjects).toContain('Messaging')
  })

  it('MODERATOR cannot manage Users or System', () => {
    const abilities = mapRoleToAbilities('MODERATOR')

    const subjects = abilities.map(a => a.subject)

    expect(subjects).not.toContain('Users')
    expect(subjects).not.toContain('System')
    expect(subjects).not.toContain('Settings')
  })

  it('STUDENT gets only read:Dashboard', () => {
    const abilities = mapRoleToAbilities('STUDENT')

    expect(abilities).toEqual([{ action: 'read', subject: 'Dashboard' }])
  })

  it('unknown role returns empty abilities', () => {
    const abilities = mapRoleToAbilities('UNKNOWN_ROLE')

    expect(abilities).toEqual([])
  })

  it('MODERATOR has read-only Analytics and Pedagogy', () => {
    const abilities = mapRoleToAbilities('MODERATOR')

    const analyticsRule = abilities.find(a => a.subject === 'Analytics')
    const pedagogyRule = abilities.find(a => a.subject === 'Pedagogy')

    expect(analyticsRule).toEqual({ action: 'read', subject: 'Analytics' })
    expect(pedagogyRule).toEqual({ action: 'read', subject: 'Pedagogy' })
  })
})
