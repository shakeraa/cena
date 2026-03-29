import type { Actions, Subjects } from '@/plugins/casl/ability'

interface AbilityRule {
  action: Actions
  subject: Subjects
}

export function mapRoleToAbilities(role: string): AbilityRule[] {
  switch (role) {
    case 'SUPER_ADMIN':
      return [{ action: 'manage', subject: 'all' }]
    case 'ADMIN':
      return [
        { action: 'manage', subject: 'Users' },
        { action: 'manage', subject: 'Content' },
        { action: 'manage', subject: 'Questions' },
        { action: 'manage', subject: 'Analytics' },
        { action: 'manage', subject: 'Focus' },
        { action: 'manage', subject: 'Mastery' },
        { action: 'manage', subject: 'Outreach' },
        { action: 'manage', subject: 'Pedagogy' },
        { action: 'manage', subject: 'Tutoring' },
        { action: 'read', subject: 'System' },
        { action: 'read', subject: 'AuditLog' },
        { action: 'manage', subject: 'Settings' },
      ]
    case 'MODERATOR':
      return [
        { action: 'manage', subject: 'Content' },
        { action: 'manage', subject: 'Questions' },
        { action: 'read', subject: 'Analytics' },
        { action: 'read', subject: 'Pedagogy' },
        { action: 'read', subject: 'Tutoring' },
      ]
    case 'STUDENT':
      return [{ action: 'read', subject: 'Dashboard' }]
    default:
      return []
  }
}
