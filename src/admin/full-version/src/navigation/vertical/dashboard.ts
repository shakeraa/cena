export default [
  {
    title: 'Dashboards',
    icon: { icon: 'tabler-layout-dashboard' },
    children: [
      {
        title: 'Platform Overview',
        to: 'dashboards-admin',
        icon: { icon: 'tabler-chart-pie' },
        action: 'read',
        subject: 'Analytics',
      },
      {
        title: 'Focus & Attention',
        to: 'apps-focus-dashboard',
        icon: { icon: 'tabler-eye-check' },
        action: 'read',
        subject: 'Focus',
      },
      {
        title: 'Mastery Progress',
        to: 'apps-mastery-dashboard',
        icon: { icon: 'tabler-graph' },
        action: 'read',
        subject: 'Mastery',
      },
      {
        title: 'Outreach & Engagement',
        to: 'apps-outreach-dashboard',
        icon: { icon: 'tabler-bell-ringing' },
        action: 'read',
        subject: 'Outreach',
      },
    ],
  },
]
