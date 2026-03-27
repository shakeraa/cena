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
        icon: { icon: 'tabler-eye-check' },
        action: 'read',
        subject: 'Focus',
        disabled: true,
      },
      {
        title: 'Mastery Progress',
        icon: { icon: 'tabler-graph' },
        action: 'read',
        subject: 'Mastery',
        disabled: true,
      },
      {
        title: 'Outreach & Engagement',
        icon: { icon: 'tabler-bell-ringing' },
        action: 'read',
        subject: 'Outreach',
        disabled: true,
      },
    ],
  },
]
