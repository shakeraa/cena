# ADM-014: Outreach & Engagement Dashboard

**Priority:** P2 — re-engagement monitoring
**Blocked by:** ADM-001 (auth), ADM-003 (permissions)
**Estimated effort:** 3 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, ApexCharts

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

Cena's `OutreachSchedulerActor` sends notifications to disengaged students via WhatsApp, Telegram, push, and voice. The admin dashboard tracks notification delivery, engagement rates, and budget usage to optimize re-engagement strategy.

## Subtasks

### ADM-014.1: Outreach Overview Dashboard

**Files to create:**

- `src/admin/full-version/src/pages/apps/outreach/dashboard.vue`
- `src/admin/full-version/src/views/apps/outreach/` — outreach components

**Widgets:**

- [ ] Notifications sent today — stat cards by channel (WhatsApp, Telegram, push, voice)
- [ ] Budget exhaustion rate — % of students who hit daily message cap
- [ ] Open/click rate by channel — bar chart
- [ ] Re-engagement rate — % of students who returned within 24h after outreach

### ADM-014.2: Channel Effectiveness Analysis

**Charts:**

- [ ] Notification volume by trigger type over time — stacked area chart
- [ ] Channel effectiveness comparison — which channel has highest re-engagement rate
- [ ] Merge effectiveness — merged vs individual messages, open rate comparison
- [ ] Optimal send time heatmap — when do students most respond

### ADM-014.3: Student Outreach History

**Acceptance:**

- [ ] Per-student outreach timeline accessible from student detail page (ADM-002)
- [ ] Shows: channel, trigger reason, message preview, delivered/opened/clicked status
- [ ] Notification preference management (opt-out channels per student)

## .NET Backend Endpoints Required

| Method | Endpoint | Description |
| ------ | -------- | ----------- |
| GET | `/api/admin/outreach/summary` | Outreach summary stats |
| GET | `/api/admin/outreach/by-channel` | Channel breakdown metrics |
| GET | `/api/admin/outreach/by-trigger` | Trigger type breakdown |
| GET | `/api/admin/outreach/re-engagement-rate` | Re-engagement trend |
| GET | `/api/admin/outreach/students/{id}/history` | Student notification history |

## Test

- [ ] Dashboard shows real notification delivery data
- [ ] Channel comparison reflects actual open/engagement rates
- [ ] Student outreach history matches OutreachSchedulerActor records
- [ ] Budget alerts fire when daily cap is approaching
