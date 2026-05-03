import type { SearchResults } from '@db/app-bar-search/types'

interface DB {
  searchItems: SearchResults[]
}

export const db: DB = {
  searchItems: [
    {
      title: 'Dashboards',
      category: 'dashboards',
      children: [
        {
          url: { name: 'dashboards-admin' },
          icon: 'tabler-layout-dashboard',
          title: 'Platform Overview',
        },
        {
          url: { name: 'apps-mastery-dashboard' },
          icon: 'tabler-brain',
          title: 'Mastery Tracking',
        },
        {
          url: { name: 'apps-focus-dashboard' },
          icon: 'tabler-focus-2',
          title: 'Focus & Engagement',
        },
        {
          url: { name: 'apps-cultural-dashboard' },
          icon: 'tabler-heart-handshake',
          title: 'Cultural Responsiveness',
        },
        {
          url: { name: 'apps-outreach-dashboard' },
          icon: 'tabler-megaphone',
          title: 'Parent Outreach',
        },
      ],
    },
    {
      title: 'Content & Pedagogy',
      category: 'contentPedagogy',
      children: [
        {
          url: { name: 'apps-questions-list' },
          icon: 'tabler-list-check',
          title: 'Question Bank',
        },
        {
          url: { name: 'apps-ingestion-pipeline' },
          icon: 'tabler-upload',
          title: 'Ingestion Pipeline',
        },
        {
          url: { name: 'apps-pedagogy-mcm-graph' },
          icon: 'tabler-git-merge',
          title: 'Concept Graph (MCM)',
        },
        {
          url: { name: 'apps-pedagogy-methodology' },
          icon: 'tabler-route',
          title: 'Methodology Configuration',
        },
        {
          url: { name: 'apps-pedagogy-methodology-hierarchy' },
          icon: 'tabler-hierarchy-3',
          title: 'Methodology Hierarchy',
        },
        {
          url: { name: 'apps-moderation-queue' },
          icon: 'tabler-shield-check',
          title: 'Moderation Queue',
        },
      ],
    },
    {
      title: 'Communication',
      category: 'communication',
      children: [
        {
          url: { name: 'apps-messaging' },
          icon: 'tabler-message-circle',
          title: 'Messaging',
        },
      ],
    },
    {
      title: 'Student AI',
      category: 'studentAi',
      children: [
        {
          url: { name: 'apps-tutoring-sessions' },
          icon: 'tabler-message-chatbot',
          title: 'Tutoring Sessions',
        },
        {
          url: { name: 'apps-experiments' },
          icon: 'tabler-flask',
          title: 'A/B Experiments',
        },
        {
          url: { name: 'apps-system-explanation-cache' },
          icon: 'tabler-database-search',
          title: 'Explanation Cache',
        },
        {
          url: { name: 'apps-system-token-budget' },
          icon: 'tabler-coins',
          title: 'Token Budget',
        },
        {
          url: { name: 'apps-system-embeddings' },
          icon: 'tabler-vector',
          title: 'Embeddings (RAG)',
        },
      ],
    },
    {
      title: 'System',
      category: 'system',
      children: [
        {
          url: { name: 'apps-system-health' },
          icon: 'tabler-heart-rate-monitor',
          title: 'System Health',
        },
        {
          url: { name: 'apps-system-actors' },
          icon: 'tabler-cpu',
          title: 'Actor Explorer',
        },
        {
          url: { name: 'apps-system-events' },
          icon: 'tabler-database',
          title: 'Event Store',
        },
        {
          url: { name: 'apps-system-dead-letters' },
          icon: 'tabler-mail-off',
          title: 'Dead Letters',
        },
        {
          url: { name: 'apps-system-audit-log' },
          icon: 'tabler-file-search',
          title: 'Audit Log',
        },
        {
          url: { name: 'apps-system-ai-settings' },
          icon: 'tabler-robot',
          title: 'AI Settings',
        },
        {
          url: { name: 'apps-system-architecture' },
          icon: 'tabler-sitemap',
          title: 'Architecture',
        },
        {
          url: { name: 'apps-system-settings' },
          icon: 'tabler-settings',
          title: 'Platform Settings',
        },
      ],
    },
    {
      title: 'Admin',
      category: 'admin',
      children: [
        {
          url: { name: 'apps-user-list' },
          icon: 'tabler-users-group',
          title: 'User Management',
        },
        {
          url: { name: 'apps-roles' },
          icon: 'tabler-shield-checkered',
          title: 'Roles',
        },
        {
          url: { name: 'apps-permissions' },
          icon: 'tabler-shield-checkered',
          title: 'Permissions',
        },
        {
          url: { name: 'pages-account-settings-tab', params: { tab: 'account' } },
          icon: 'tabler-user-circle',
          title: 'Account Settings',
        },
      ],
    },
  ],
}
