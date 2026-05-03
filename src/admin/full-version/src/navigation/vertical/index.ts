import appsAndPages from './apps-and-pages'
import dashboard from './dashboard'
import type { VerticalNavItems } from '@layouts/types'

export default [...dashboard, ...appsAndPages] as VerticalNavItems
