export type CenaUserRole = 'STUDENT' | 'TEACHER' | 'PARENT' | 'MODERATOR' | 'ADMIN' | 'SUPER_ADMIN'
export type CenaUserStatus = 'active' | 'suspended' | 'pending'

export interface CenaUserProperties {
  id: string
  uid: string
  fullName: string
  email: string
  role: CenaUserRole
  status: CenaUserStatus
  school: string
  grade: string
  avatar: string
  locale: string
  createdAt: string
  lastLoginAt: string | null
}
