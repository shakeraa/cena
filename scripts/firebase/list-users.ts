/**
 * List Firebase users and their custom claims.
 *
 * Usage:
 *   npx tsx scripts/firebase/list-users.ts
 *   npx tsx scripts/firebase/list-users.ts --role ADMIN
 */

import { cert, initializeApp } from 'firebase-admin/app'
import { getAuth } from 'firebase-admin/auth'
import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = dirname(fileURLToPath(import.meta.url))
const serviceAccountPath = resolve(__dirname, 'service-account-key.json')

let serviceAccount: Record<string, unknown>
try {
  serviceAccount = JSON.parse(readFileSync(serviceAccountPath, 'utf-8'))
}
catch {
  console.error(`Service account key not found at: ${serviceAccountPath}`)
  console.error('Run set-admin-claims.ts for setup instructions.')
  process.exit(1)
}

const app = initializeApp({ credential: cert(serviceAccount) })
const auth = getAuth(app)

const roleFilter = process.argv.includes('--role') ? process.argv[process.argv.indexOf('--role') + 1] : null

async function listUsers() {
  let pageToken: string | undefined
  let total = 0

  console.log('\nCena Firebase Users')
  console.log('='.repeat(80))
  console.log(`${'Email'.padEnd(35)} ${'Role'.padEnd(15)} ${'Locale'.padEnd(8)} ${'School'.padEnd(15)} UID`)
  console.log('-'.repeat(80))

  do {
    const result = await auth.listUsers(100, pageToken)

    for (const user of result.users) {
      const claims = user.customClaims || {}
      const role = (claims.role as string) || '-'

      if (roleFilter && role !== roleFilter)
        continue

      const locale = (claims.locale as string) || '-'
      const school = (claims.school_id as string) || '-'

      console.log(`${(user.email || '-').padEnd(35)} ${role.padEnd(15)} ${locale.padEnd(8)} ${school.padEnd(15)} ${user.uid}`)
      total++
    }

    pageToken = result.pageToken
  } while (pageToken)

  console.log('-'.repeat(80))
  console.log(`Total: ${total} users${roleFilter ? ` (filtered: ${roleFilter})` : ''}`)
}

listUsers()
