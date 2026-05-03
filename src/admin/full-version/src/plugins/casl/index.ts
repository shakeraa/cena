import type { App } from 'vue'

import { abilitiesPlugin } from '@casl/vue'
import { ability } from './ability'
import type { Rule } from './ability'

export default function (app: App) {
  // Restore abilities from cookie on app startup
  const userAbilityRules = useCookie<Rule[]>('userAbilityRules')
  if (userAbilityRules.value?.length) {
    ability.update(userAbilityRules.value)
  }

  // Use the SAME ability instance that useFirebaseAuth updates
  app.use(abilitiesPlugin, ability, {
    useGlobalProperties: true,
  })
}
