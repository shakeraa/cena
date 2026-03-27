/**
 * Set Firebase custom claims for a Cena admin user.
 *
 * Prerequisites:
 *   1. Download your Firebase service account key from:
 *      Firebase Console > Project Settings > Service Accounts > Generate New Private Key
 *   2. Save it as: scripts/firebase/service-account-key.json
 *
 * Usage:
 *   npx tsx scripts/firebase/set-admin-claims.ts <email> <role>
 *
 * Examples:
 *   npx tsx scripts/firebase/set-admin-claims.ts shaker@cena.edu SUPER_ADMIN
 *   npx tsx scripts/firebase/set-admin-claims.ts moderator@cena.edu MODERATOR
 *   npx tsx scripts/firebase/set-admin-claims.ts admin@school.edu ADMIN --school-id sch_abc123
 *
 * Valid roles: STUDENT, TEACHER, PARENT, MODERATOR, ADMIN, SUPER_ADMIN
 */

import { cert, initializeApp } from 'firebase-admin/app'
import { getAuth } from 'firebase-admin/auth'
import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = dirname(fileURLToPath(import.meta.url))

const VALID_ROLES = ['STUDENT', 'TEACHER', 'PARENT', 'MODERATOR', 'ADMIN', 'SUPER_ADMIN'] as const
type CenaRole = typeof VALID_ROLES[number]

// Parse args
const args = process.argv.slice(2)
const email = args[0]
const role = args[1] as CenaRole

if (!email || !role) {
  console.error('Usage: npx tsx scripts/firebase/set-admin-claims.ts <email> <role> [--school-id <id>] [--locale <he|ar|en>]')
  console.error('Roles:', VALID_ROLES.join(', '))
  process.exit(1)
}

if (!VALID_ROLES.includes(role)) {
  console.error(`Invalid role: "${role}". Must be one of: ${VALID_ROLES.join(', ')}`)
  process.exit(1)
}

// Parse optional flags
let schoolId: string | undefined
let locale = 'en'
let plan = 'free'

for (let i = 2; i < args.length; i++) {
  if (args[i] === '--school-id' && args[i + 1]) {
    schoolId = args[++i]
  }
  else if (args[i] === '--locale' && args[i + 1]) {
    locale = args[++i]
  }
  else if (args[i] === '--plan' && args[i + 1]) {
    plan = args[++i]
  }
}

// Initialize Firebase Admin
const serviceAccountPath = resolve(__dirname, 'service-account-key.json')

let serviceAccount: Record<string, unknown>
try {
  serviceAccount = JSON.parse(readFileSync(serviceAccountPath, 'utf-8'))
}
catch {
  console.error(`\nService account key not found at: ${serviceAccountPath}`)
  console.error('\nTo get it:')
  console.error('  1. Go to Firebase Console > Project Settings > Service Accounts')
  console.error('  2. Click "Generate New Private Key"')
  console.error('  3. Save the file as: scripts/firebase/service-account-key.json')
  console.error('\nIMPORTANT: Never commit this file to git!')
  process.exit(1)
}

const app = initializeApp({
  credential: cert(serviceAccount),
})

const auth = getAuth(app)

async function setCustomClaims() {
  try {
    // Look up user by email
    const user = await auth.getUserByEmail(email)

    console.log(`\nFound user: ${user.displayName || user.email} (${user.uid})`)
    console.log(`Current claims: ${JSON.stringify(user.customClaims || {})}`)

    // Build custom claims per contracts/backend/firebase-auth.md
    const claims: Record<string, unknown> = {
      role,
      locale,
      plan,
    }

    if (schoolId)
      claims.school_id = schoolId

    // Set custom claims
    await auth.setCustomUserClaims(user.uid, claims)

    console.log(`\nCustom claims set successfully:`)
    console.log(JSON.stringify(claims, null, 2))
    console.log(`\nNote: The user must sign out and sign back in for claims to take effect.`)
    console.log(`Alternatively, call currentUser.getIdToken(true) to force a token refresh.`)

    // Verify
    const updatedUser = await auth.getUser(user.uid)

    console.log(`\nVerification — updated claims: ${JSON.stringify(updatedUser.customClaims)}`)
  }
  catch (error: unknown) {
    const err = error as { code?: string; message?: string }

    if (err.code === 'auth/user-not-found') {
      console.error(`\nUser not found: ${email}`)
      console.error('Create the user first in Firebase Console > Authentication > Users')
    }
    else {
      console.error(`\nError: ${err.message}`)
    }
    process.exit(1)
  }
}

setCustomClaims()
