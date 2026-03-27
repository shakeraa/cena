export default [
  { heading: 'Content' },
  {
    title: 'Ingestion Pipeline',
    icon: { icon: 'tabler-transfer-in' },
    to: 'apps-ingestion-pipeline',
    action: 'read',
    subject: 'Content',
  },
  {
    title: 'Moderation Queue',
    icon: { icon: 'tabler-checkbox' },
    to: 'apps-moderation-queue',
    action: 'read',
    subject: 'Content',
  },
  {
    title: 'Question Bank',
    icon: { icon: 'tabler-database' },
    to: 'apps-questions-list',
    action: 'read',
    subject: 'Questions',
  },

  { heading: 'Users' },
  {
    title: 'All Users',
    icon: { icon: 'tabler-users' },
    to: 'apps-user-list',
    action: 'read',
    subject: 'Users',
  },
  {
    title: 'Roles & Permissions',
    icon: { icon: 'tabler-lock' },
    children: [
      {
        title: 'Roles',
        to: 'apps-roles',
        action: 'read',
        subject: 'Users',
      },
      {
        title: 'Permissions',
        to: 'apps-permissions',
        action: 'read',
        subject: 'Users',
      },
    ],
    action: 'read',
    subject: 'Users',
  },

  { heading: 'Pedagogy' },
  {
    title: 'Methodology Analytics',
    icon: { icon: 'tabler-school' },
    action: 'read',
    subject: 'Pedagogy',
    disabled: true,
  },
  {
    title: 'Cultural Context',
    icon: { icon: 'tabler-world' },
    action: 'read',
    subject: 'Pedagogy',
    disabled: true,
  },

  { heading: 'System' },
  {
    title: 'Actor Health',
    icon: { icon: 'tabler-heartbeat' },
    action: 'read',
    subject: 'System',
    disabled: true,
  },
  {
    title: 'Event Stream',
    icon: { icon: 'tabler-activity' },
    action: 'read',
    subject: 'System',
    disabled: true,
  },
  {
    title: 'Settings',
    icon: { icon: 'tabler-settings' },
    action: 'manage',
    subject: 'Settings',
    disabled: true,
  },
  {
    title: 'Audit Log',
    icon: { icon: 'tabler-file-analytics' },
    action: 'read',
    subject: 'AuditLog',
    disabled: true,
  },
  {
    title: 'Account Settings',
    icon: { icon: 'tabler-user-cog' },
    to: { name: 'pages-account-settings-tab', params: { tab: 'account' } },
  },
]
