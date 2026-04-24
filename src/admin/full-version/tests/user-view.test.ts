import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

// --- Template validation: ensure no missing end tags ---

function countTag(html: string, tag: string): { open: number; close: number } {
  // Strip self-closing tags (e.g. <VIcon ... /> or <VDivider />)
  const selfClosing = new RegExp(`<${tag}\\b[^>]*/\\s*>`, 'gi')
  const cleaned = html.replace(selfClosing, '')

  const openRe = new RegExp(`<${tag}(?:\\s|>)`, 'gi')
  const closeRe = new RegExp(`</${tag}\\s*>`, 'gi')

  return {
    open: (cleaned.match(openRe) || []).length,
    close: (cleaned.match(closeRe) || []).length,
  }
}

function extractTemplate(filePath: string): string {
  const content = readFileSync(filePath, 'utf-8')
  const match = content.match(/<template>([\s\S]*)<\/template>/)
  if (!match) throw new Error(`No <template> block found in ${filePath}`)
  return match[1]
}

const userViewPage = resolve(__dirname, '../src/pages/apps/user/view/[id].vue')
const userBioPanel = resolve(__dirname, '../src/views/apps/user/view/UserBioPanel.vue')
const userTabAccount = resolve(__dirname, '../src/views/apps/user/view/UserTabAccount.vue')
const masteryStudentPage = resolve(__dirname, '../src/pages/apps/mastery/student/[id].vue')

describe('template structure', () => {
  const pages = [
    { name: 'UserViewPage', path: userViewPage },
    { name: 'UserBioPanel', path: userBioPanel },
    { name: 'UserTabAccount', path: userTabAccount },
    { name: 'MasteryStudentPage', path: masteryStudentPage },
  ]

  for (const { name, path } of pages) {
    it(`${name}: all <div> tags are balanced`, () => {
      const template = extractTemplate(path)
      const { open, close } = countTag(template, 'div')
      expect(close).toBe(open)
    })

    it(`${name}: all <VCard> tags are balanced`, () => {
      const template = extractTemplate(path)
      const { open, close } = countTag(template, 'VCard')
      expect(close).toBe(open)
    })

    it(`${name}: all <VBtn> tags are balanced`, () => {
      const template = extractTemplate(path)
      const { open, close } = countTag(template, 'VBtn')
      expect(close).toBe(open)
    })

    it(`${name}: all <VCol> tags are balanced`, () => {
      const template = extractTemplate(path)
      const { open, close } = countTag(template, 'VCol')
      expect(close).toBe(open)
    })

    it(`${name}: all <VRow> tags are balanced`, () => {
      const template = extractTemplate(path)
      const { open, close } = countTag(template, 'VRow')
      expect(close).toBe(open)
    })
  }
})

// --- Tab query param resolution (extracted from [id].vue logic) ---

describe('user view tab resolution', () => {
  const tabs = [
    { key: 'overview' },
    { key: 'insights' },
    { key: 'security' },
    { key: 'activity' },
    { key: 'sessions' },
  ]

  function resolveTab(query: string | undefined): number {
    if (query) {
      const idx = tabs.findIndex(t => t.key === query)
      if (idx >= 0) return idx
    }
    return 0
  }

  it('defaults to 0 when no tab query', () => {
    expect(resolveTab(undefined)).toBe(0)
  })

  it('resolves "insights" to tab index 1', () => {
    expect(resolveTab('insights')).toBe(1)
  })

  it('resolves "security" to tab index 2', () => {
    expect(resolveTab('security')).toBe(2)
  })

  it('resolves "activity" to tab index 3', () => {
    expect(resolveTab('activity')).toBe(3)
  })

  it('resolves "sessions" to tab index 4', () => {
    expect(resolveTab('sessions')).toBe(4)
  })

  it('falls back to 0 for unknown tab', () => {
    expect(resolveTab('nonexistent')).toBe(0)
  })
})

// --- UserBioPanel helper functions ---

describe('UserBioPanel helpers', () => {
  const resolveUserRoleVariant = (role: string) => {
    const map: Record<string, { color: string; icon: string }> = {
      SUPER_ADMIN: { color: 'error', icon: 'tabler-crown' },
      ADMIN: { color: 'error', icon: 'tabler-shield' },
      MODERATOR: { color: 'warning', icon: 'tabler-eye-check' },
      TEACHER: { color: 'info', icon: 'tabler-chalkboard' },
      STUDENT: { color: 'success', icon: 'tabler-school' },
      PARENT: { color: 'secondary', icon: 'tabler-users' },
    }
    return map[role] ?? { color: 'primary', icon: 'tabler-user' }
  }

  const resolveStatusVariant = (status: string) => {
    const map: Record<string, string> = {
      active: 'success',
      suspended: 'error',
      pending: 'warning',
    }
    return map[status] ?? 'primary'
  }

  const formatLocale = (locale: string) => {
    const map: Record<string, string> = { en: 'English', he: 'Hebrew', ar: 'Arabic' }
    return map[locale] ?? locale
  }

  const formatDate = (dateStr: string | null) => {
    if (!dateStr) return 'Never'
    return new Date(dateStr).toLocaleDateString('en-US', {
      year: 'numeric', month: 'short', day: 'numeric',
      hour: '2-digit', minute: '2-digit',
    })
  }

  it('resolves STUDENT role', () => {
    expect(resolveUserRoleVariant('STUDENT')).toEqual({ color: 'success', icon: 'tabler-school' })
  })

  it('resolves ADMIN role', () => {
    expect(resolveUserRoleVariant('ADMIN')).toEqual({ color: 'error', icon: 'tabler-shield' })
  })

  it('falls back for unknown role', () => {
    expect(resolveUserRoleVariant('UNKNOWN')).toEqual({ color: 'primary', icon: 'tabler-user' })
  })

  it('resolves active status', () => {
    expect(resolveStatusVariant('active')).toBe('success')
  })

  it('resolves suspended status', () => {
    expect(resolveStatusVariant('suspended')).toBe('error')
  })

  it('falls back for unknown status', () => {
    expect(resolveStatusVariant('UNKNOWN')).toBe('primary')
  })

  it('formats Hebrew locale', () => {
    expect(formatLocale('he')).toBe('Hebrew')
  })

  it('formats English locale', () => {
    expect(formatLocale('en')).toBe('English')
  })

  it('passes through unknown locale', () => {
    expect(formatLocale('fr')).toBe('fr')
  })

  it('formats null date as Never', () => {
    expect(formatDate(null)).toBe('Never')
  })

  it('formats a valid date string', () => {
    const result = formatDate('2026-02-01T00:00:00Z')
    expect(result).toContain('2026')
    expect(result).toContain('Feb')
  })
})

// --- Bio panel button layout ---

describe('UserBioPanel button layout', () => {
  it('action buttons container uses flex-wrap', () => {
    const content = readFileSync(userBioPanel, 'utf-8')
    expect(content).toContain('flex-wrap')
  })
})

// --- Route links ---

describe('navigation links', () => {
  it('UserBioPanel mastery link uses correct path pattern', () => {
    const content = readFileSync(userBioPanel, 'utf-8')
    expect(content).toContain('/apps/mastery/student/')
  })

  it('UserBioPanel insights link uses query param', () => {
    const content = readFileSync(userBioPanel, 'utf-8')
    expect(content).toContain("tab: 'insights'")
  })

  it('UserViewPage reads tab from query on init', () => {
    const content = readFileSync(userViewPage, 'utf-8')
    expect(content).toContain('route.query.tab')
  })

  it('UserViewPage watches query.tab for reactive updates', () => {
    const content = readFileSync(userViewPage, 'utf-8')
    expect(content).toContain('watch(() => route.query.tab')
  })

  it('mastery student page exists with correct route name', () => {
    const content = readFileSync(masteryStudentPage, 'utf-8')
    expect(content).toContain("useRoute('apps-mastery-student-id')")
  })
})
