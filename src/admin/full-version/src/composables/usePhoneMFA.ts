import {
  PhoneAuthProvider,
  PhoneMultiFactorGenerator,
  multiFactor,
  type MultiFactorInfo,
} from 'firebase/auth'
import { firebaseAuth } from '@/plugins/firebase'

export function usePhoneMFA() {
  const isEnrolling = ref(false)
  const verificationId = ref<string | null>(null)
  const error = ref<string | null>(null)
  const enrolledFactors = ref<MultiFactorInfo[]>([])

  const getMFAStatus = () => {
    const user = firebaseAuth.currentUser
    if (!user) return []
    enrolledFactors.value = multiFactor(user).enrolledFactors
    return enrolledFactors.value
  }

  const enrollPhoneMFA = async (phoneNumber: string, recaptchaVerifier: any) => {
    error.value = null
    isEnrolling.value = true
    try {
      const user = firebaseAuth.currentUser
      if (!user) throw new Error('No authenticated user')

      const session = await multiFactor(user).getSession()
      const phoneInfoOptions = {
        phoneNumber,
        session,
      }

      const phoneAuthProvider = new PhoneAuthProvider(firebaseAuth)
      verificationId.value = await phoneAuthProvider.verifyPhoneNumber(phoneInfoOptions, recaptchaVerifier)

      return verificationId.value
    }
    catch (e: any) {
      error.value = e.message || 'Failed to start MFA enrollment'
      throw e
    }
    finally {
      isEnrolling.value = false
    }
  }

  const confirmEnrollment = async (verId: string, smsCode: string) => {
    error.value = null
    try {
      const user = firebaseAuth.currentUser
      if (!user) throw new Error('No authenticated user')

      const cred = PhoneAuthProvider.credential(verId, smsCode)
      const multiFactorAssertion = PhoneMultiFactorGenerator.assertion(cred)
      await multiFactor(user).enroll(multiFactorAssertion, 'Phone')

      getMFAStatus()
    }
    catch (e: any) {
      error.value = e.message || 'Failed to confirm MFA enrollment'
      throw e
    }
  }

  const unenrollMFA = async (factorIndex = 0) => {
    error.value = null
    try {
      const user = firebaseAuth.currentUser
      if (!user) throw new Error('No authenticated user')

      const factors = multiFactor(user).enrolledFactors
      if (factors.length <= factorIndex) throw new Error('No MFA factor at given index')

      await multiFactor(user).unenroll(factors[factorIndex])
      getMFAStatus()
    }
    catch (e: any) {
      error.value = e.message || 'Failed to unenroll MFA'
      throw e
    }
  }

  return {
    isEnrolling,
    verificationId,
    error,
    enrolledFactors,
    getMFAStatus,
    enrollPhoneMFA,
    confirmEnrollment,
    unenrollMFA,
  }
}
