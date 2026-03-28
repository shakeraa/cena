import { createMongoAbility } from '@casl/ability'

export type Actions = 'create' | 'read' | 'update' | 'delete' | 'manage'

export type Subjects =
  | 'all'
  | 'Users'
  | 'Content'
  | 'Questions'
  | 'Analytics'
  | 'Focus'
  | 'Mastery'
  | 'Outreach'
  | 'Pedagogy'
  | 'Tutoring'
  | 'System'
  | 'AuditLog'
  | 'Settings'

export type CenaRole = 'STUDENT' | 'TEACHER' | 'PARENT' | 'MODERATOR' | 'ADMIN' | 'SUPER_ADMIN'

export interface Rule { action: Actions; subject: Subjects }

export const ability = createMongoAbility<[Actions, Subjects]>()
