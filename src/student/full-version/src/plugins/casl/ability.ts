import { createMongoAbility } from '@casl/ability'

export type Actions = 'create' | 'read' | 'update' | 'delete' | 'manage'

export type Subjects =
  | 'all'
  | 'Home'
  | 'Session'
  | 'Tutor'
  | 'Challenges'
  | 'KnowledgeGraph'
  | 'Progress'
  | 'Social'
  | 'Notifications'
  | 'Profile'
  | 'Settings'

export interface Rule { action: Actions; subject: Subjects }

export const ability = createMongoAbility<[Actions, Subjects]>()

/**
 * Default ability rules for authenticated students.
 *
 * The student app has no role-based menu hiding: every signed-in student
 * sees the same navigation. We grant `manage` on `all` so that any nav
 * item that specifies an `action`+`subject` pair is automatically visible.
 *
 * FIND-ux-020: this constant is consumed by authStore.__mockSignIn,
 * the firebase.ts hydration path, and (when wired) the real Firebase
 * onAuthStateChanged handler. It is also persisted to the
 * `userAbilityRules` cookie so the CASL plugin can rehydrate on refresh.
 */
export const studentAbilityRules: Rule[] = [
  { action: 'manage', subject: 'all' },
]
