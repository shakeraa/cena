import type { VerticalNavItems } from '@layouts/types'

// Student sidebar per docs/student/01-navigation-and-ia.md §Navigation Structure.
// The `badge` slot on "Start Session" is populated dynamically by the nav shell
// from the active-session poll (see meStore + useActiveSessionBadge).
//
// i18n: every `title` and `heading` is an i18n key resolved by getDynamicI18nProps
// (see src/@layouts/utils.ts:93). Keys live under `nav.*` and `navSection.*` in
// the three locale bundles (en/he/ar).
const navItems: VerticalNavItems = [
  { heading: 'navSection.learn' },
  {
    title: 'nav.home',
    icon: { icon: 'tabler-home' },
    to: { name: 'home' },
  },
  {
    title: 'nav.startSession',
    icon: { icon: 'tabler-player-play' },
    to: { name: 'session' },
  },
  {
    title: 'nav.aiTutor',
    icon: { icon: 'tabler-message-chatbot' },
    to: { name: 'tutor' },
  },
  // RDY-056: student photo + PDF upload flows drive the Phase 2.1 / 2.2
  // ingestion endpoints.
  {
    title: 'nav.snapProblem',
    icon: { icon: 'tabler-camera' },
    to: { name: 'tutor-photo-capture' },
  },
  {
    title: 'nav.uploadProblem',
    icon: { icon: 'tabler-upload' },
    to: { name: 'tutor-pdf-upload' },
  },

  { heading: 'navSection.practice' },
  {
    title: 'nav.challenges',
    icon: { icon: 'tabler-swords' },
    children: [
      { title: 'nav.dailyChallenge', to: { name: 'challenges-daily' } },
      { title: 'nav.bossBattles', to: { name: 'challenges-boss' } },
      { title: 'nav.cardChain', to: { name: 'challenges' } },
    ],
  },
  {
    title: 'nav.knowledgeGraph',
    icon: { icon: 'tabler-affiliate' },
    to: { name: 'knowledge-graph' },
  },

  { heading: 'navSection.progress' },
  {
    title: 'nav.overview',
    icon: { icon: 'tabler-chart-line' },
    to: { name: 'progress' },
  },
  {
    title: 'nav.sessionHistory',
    icon: { icon: 'tabler-history' },
    to: { name: 'progress-sessions' },
  },
  {
    title: 'nav.mastery',
    icon: { icon: 'tabler-target' },
    to: { name: 'progress-mastery' },
  },
  {
    title: 'nav.learningTime',
    icon: { icon: 'tabler-clock' },
    to: { name: 'progress-time' },
  },

  { heading: 'navSection.community' },
  {
    title: 'nav.classFeed',
    icon: { icon: 'tabler-users' },
    to: { name: 'social' },
  },
  {
    title: 'nav.leaderboard',
    icon: { icon: 'tabler-trophy' },
    to: { name: 'social-leaderboard' },
  },
  {
    title: 'nav.peerSolutions',
    icon: { icon: 'tabler-bulb' },
    to: { name: 'social-peers' },
  },

  { heading: 'navSection.account' },
  {
    title: 'nav.notifications',
    icon: { icon: 'tabler-bell' },
    to: { name: 'notifications' },
  },
  {
    title: 'nav.profile',
    icon: { icon: 'tabler-user' },
    to: { name: 'profile' },
  },
  {
    title: 'nav.settings',
    icon: { icon: 'tabler-settings' },
    to: { name: 'settings' },
  },
]

export default navItems
