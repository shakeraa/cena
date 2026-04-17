import type { VerticalNavItems } from '@layouts/types'

// Student sidebar per docs/student/01-navigation-and-ia.md §Navigation Structure.
// The `badge` slot on "Start Session" is populated dynamically by the nav shell
// from the active-session poll (see meStore + useActiveSessionBadge).
const navItems: VerticalNavItems = [
  { heading: 'Learn' },
  {
    title: 'Home',
    icon: { icon: 'tabler-home' },
    to: { name: 'home' },
  },
  {
    title: 'Start Session',
    icon: { icon: 'tabler-player-play' },
    to: { name: 'session' },
  },
  {
    title: 'AI Tutor',
    icon: { icon: 'tabler-message-chatbot' },
    to: { name: 'tutor' },
  },
  // RDY-056: student photo + PDF upload flows drive the Phase 2.1 / 2.2
  // ingestion endpoints.
  {
    title: 'Snap Problem',
    icon: { icon: 'tabler-camera' },
    to: { name: 'tutor-photo-capture' },
  },
  {
    title: 'Upload Problem',
    icon: { icon: 'tabler-upload' },
    to: { name: 'tutor-pdf-upload' },
  },

  { heading: 'Practice' },
  {
    title: 'Challenges',
    icon: { icon: 'tabler-swords' },
    children: [
      { title: 'Daily Challenge', to: { name: 'challenges-daily' } },
      { title: 'Boss Battles', to: { name: 'challenges-boss' } },
      { title: 'Card Chains', to: { name: 'challenges' } },
    ],
  },
  {
    title: 'Knowledge Graph',
    icon: { icon: 'tabler-affiliate' },
    to: { name: 'knowledge-graph' },
  },

  { heading: 'Progress' },
  {
    title: 'Overview',
    icon: { icon: 'tabler-chart-line' },
    to: { name: 'progress' },
  },
  {
    title: 'Session History',
    icon: { icon: 'tabler-history' },
    to: { name: 'progress-sessions' },
  },
  {
    title: 'Mastery',
    icon: { icon: 'tabler-target' },
    to: { name: 'progress-mastery' },
  },
  {
    title: 'Learning Time',
    icon: { icon: 'tabler-clock' },
    to: { name: 'progress-time' },
  },

  { heading: 'Community' },
  {
    title: 'Class Feed',
    icon: { icon: 'tabler-users' },
    to: { name: 'social' },
  },
  {
    title: 'Leaderboard',
    icon: { icon: 'tabler-trophy' },
    to: { name: 'social-leaderboard' },
  },
  {
    title: 'Peer Solutions',
    icon: { icon: 'tabler-bulb' },
    to: { name: 'social-peers' },
  },

  { heading: 'Account' },
  {
    title: 'Notifications',
    icon: { icon: 'tabler-bell' },
    to: { name: 'notifications' },
  },
  {
    title: 'Profile',
    icon: { icon: 'tabler-user' },
    to: { name: 'profile' },
  },
  {
    title: 'Settings',
    icon: { icon: 'tabler-settings' },
    to: { name: 'settings' },
  },
]

export default navItems
