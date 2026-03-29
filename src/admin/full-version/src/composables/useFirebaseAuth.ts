import {
  GoogleAuthProvider,
  OAuthProvider,
  onAuthStateChanged,
  sendPasswordResetEmail,
  signInWithEmailAndPassword,
  signInWithPopup,
  signOut,
} from 'firebase/auth'
import type { User as FirebaseUser } from 'firebase/auth'
import { firebaseAuth } from '@/plugins/firebase'
import { ability } from '@/plugins/casl/ability'
import type { CenaRole } from '@/plugins/casl/ability'
import { mapRoleToAbilities } from '@/plugins/casl/role-abilities'

export interface CenaUser {
  uid: string
  email: string | null
  displayName: string | null
  photoURL: string | null
  role: CenaRole
  schoolId?: string
  locale: string
  plan: string
}

const ADMIN_ROLES: CenaRole[] = ['MODERATOR', 'ADMIN', 'SUPER_ADMIN']

function extractCenaUser(firebaseUser: FirebaseUser, claims: Record<string, unknown>): CenaUser {
  return {
    uid: firebaseUser.uid,
    email: firebaseUser.email,
    displayName: firebaseUser.displayName,
    photoURL: firebaseUser.photoURL,
    role: (claims.role as CenaRole) || 'STUDENT',
    schoolId: claims.school_id as string | undefined,
    locale: (claims.locale as string) || 'en',
    plan: (claims.plan as string) || 'free',
  }
}

export function useFirebaseAuth() {
  const currentUser = ref<CenaUser | null>(null)
  const isLoading = ref(true)
  const authError = ref<string | null>(null)

  // Watch auth state
  const initAuth = () => {
    return new Promise<CenaUser | null>((resolve) => {
      const unsubscribe = onAuthStateChanged(firebaseAuth, async (firebaseUser) => {
        if (firebaseUser) {
          const tokenResult = await firebaseUser.getIdTokenResult()
          const claims = tokenResult.claims as Record<string, unknown>
          const role = (claims.role as CenaRole) || 'STUDENT'

          if (!ADMIN_ROLES.includes(role)) {
            // Non-admin user — sign them out of admin dashboard
            await signOut(firebaseAuth)
            currentUser.value = null
            authError.value = 'Access denied. Admin, Moderator, or Super Admin role required.'
            isLoading.value = false
            resolve(null)

            return
          }

          const user = extractCenaUser(firebaseUser, claims)

          currentUser.value = user

          // Update CASL abilities
          const abilities = mapRoleToAbilities(role)

          ability.update(abilities)

          // Store in cookies for Vuexy compatibility
          useCookie('userData').value = user as any
          useCookie('accessToken').value = tokenResult.token
          useCookie('userAbilityRules').value = abilities as any

          isLoading.value = false
          resolve(user)
        }
        else {
          currentUser.value = null
          ability.update([])

          useCookie('userData').value = null
          useCookie('accessToken').value = null
          useCookie('userAbilityRules').value = null

          isLoading.value = false
          resolve(null)
        }
        unsubscribe()
      })
    })
  }

  const loginWithEmail = async (email: string, password: string) => {
    authError.value = null
    try {
      const credential = await signInWithEmailAndPassword(firebaseAuth, email, password)
      const tokenResult = await credential.user.getIdTokenResult(true)
      const claims = tokenResult.claims as Record<string, unknown>
      const role = (claims.role as CenaRole) || 'STUDENT'

      if (!ADMIN_ROLES.includes(role)) {
        await signOut(firebaseAuth)
        throw new Error('Access denied. Admin, Moderator, or Super Admin role required.')
      }

      const user = extractCenaUser(credential.user, claims)

      currentUser.value = user

      const abilities = mapRoleToAbilities(role)

      ability.update(abilities)

      useCookie('userData').value = user as any
      useCookie('accessToken').value = tokenResult.token
      useCookie('userAbilityRules').value = abilities as any

      return user
    }
    catch (error: unknown) {
      const err = error as { code?: string; message?: string }

      switch (err.code) {
        case 'auth/user-not-found':
        case 'auth/wrong-password':
        case 'auth/invalid-credential':
          authError.value = 'Invalid email or password'
          break
        case 'auth/user-disabled':
          authError.value = 'This account has been disabled'
          break
        case 'auth/too-many-requests':
          authError.value = 'Too many failed attempts. Please try again later.'
          break
        default:
          authError.value = err.message || 'Login failed'
      }
      throw error
    }
  }

  const loginWithGoogle = async () => {
    authError.value = null
    try {
      const provider = new GoogleAuthProvider()
      const credential = await signInWithPopup(firebaseAuth, provider)

      // Force fresh token to get latest custom claims
      const tokenResult = await credential.user.getIdTokenResult(true)
      const claims = tokenResult.claims as Record<string, unknown>
      const role = (claims.role as CenaRole) || 'STUDENT'

      if (!ADMIN_ROLES.includes(role)) {
        await signOut(firebaseAuth)
        throw new Error('Access denied. Admin, Moderator, or Super Admin role required.')
      }

      const user = extractCenaUser(credential.user, claims)

      currentUser.value = user

      const abilities = mapRoleToAbilities(role)

      ability.update(abilities)

      useCookie('userData').value = user as any
      useCookie('accessToken').value = tokenResult.token
      useCookie('userAbilityRules').value = abilities as any

      return user
    }
    catch (error: unknown) {
      const err = error as { code?: string; message?: string }

      authError.value = err.message || 'Google sign-in failed'
      throw error
    }
  }

  const loginWithApple = async () => {
    authError.value = null
    try {
      const provider = new OAuthProvider('apple.com')
      provider.addScope('email')
      provider.addScope('name')
      const credential = await signInWithPopup(firebaseAuth, provider)

      const tokenResult = await credential.user.getIdTokenResult(true)
      const claims = tokenResult.claims as Record<string, unknown>
      const role = (claims.role as CenaRole) || 'STUDENT'

      if (!ADMIN_ROLES.includes(role)) {
        await signOut(firebaseAuth)
        throw new Error('Access denied. Admin, Moderator, or Super Admin role required.')
      }

      const user = extractCenaUser(credential.user, claims)

      currentUser.value = user
      const abilities = mapRoleToAbilities(role)

      ability.update(abilities)
      useCookie('userData').value = user as any
      useCookie('accessToken').value = tokenResult.token
      useCookie('userAbilityRules').value = abilities as any

      return user
    }
    catch (error: unknown) {
      const err = error as { code?: string; message?: string }

      authError.value = err.message || 'Apple sign-in failed'
      throw error
    }
  }

  const logout = async () => {
    await signOut(firebaseAuth)
    currentUser.value = null
    ability.update([])
    useCookie('userData').value = null
    useCookie('accessToken').value = null
    useCookie('userAbilityRules').value = null
  }

  const resetPassword = async (email: string) => {
    authError.value = null
    try {
      await sendPasswordResetEmail(firebaseAuth, email)
    }
    catch (error: unknown) {
      const err = error as { code?: string; message?: string }

      switch (err.code) {
        case 'auth/user-not-found':
          // Don't reveal if email exists
          break
        case 'auth/too-many-requests':
          authError.value = 'Too many attempts. Please try again later.'
          throw error
        default:
          authError.value = err.message || 'Failed to send reset email'
          throw error
      }
    }
  }

  const getIdToken = async (forceRefresh = false): Promise<string | null> => {
    const user = firebaseAuth.currentUser
    if (!user)
      return null

    const token = await user.getIdToken(forceRefresh)

    useCookie('accessToken').value = token

    return token
  }

  return {
    currentUser,
    isLoading,
    authError,
    initAuth,
    loginWithEmail,
    loginWithGoogle,
    loginWithApple,
    logout,
    resetPassword,
    getIdToken,
  }
}
