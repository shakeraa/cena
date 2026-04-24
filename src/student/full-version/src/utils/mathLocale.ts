// =============================================================================
// PRR-031 / PRR-032 — Math locale helpers: numerals + spoken labels.
//
// Three concerns live here (tightly coupled, small enough that splitting
// would create more friction than it saves):
//
//   1. Numerals preference inference + conversion (western ↔ eastern).
//      Eastern Arabic numerals (٠-٩) are common in Arab-sector K-12
//      textbooks outside the Levant; Western digits (0-9) are standard
//      in Israeli Bagrut texts and most computer-presented math.
//      Default inference:
//        - ar locale → 'eastern' (matches Tawjihi / PA textbooks)
//        - he, en    → 'western'
//      Students can override via onboarding / settings. The choice is
//      persisted in the onboarding store; this module is stateless.
//
//   2. LaTeX-to-spoken text conversion for aria-label production.
//      Mirrors a subset of the authoritative server-side lexicon in
//      src/shared/Cena.Infrastructure/Accessibility/MathAriaLabels.cs.
//      On the frontend we only need a reduced set because:
//        - Server-issued content includes pre-computed aria labels.
//        - Client-side dynamic math (hint ladder, faded examples) is
//          simpler and bounded.
//      The server is authoritative; this is a fast-path fallback.
//
//   3. HTML-scoped digit swap. We only touch text nodes inside KaTeX's
//      rendered HTML to avoid breaking class names, aria attributes, or
//      inline SVG numbers in font glyphs. A regex over digit runs that
//      are NOT inside angle-bracketed tag content is sufficient.
// =============================================================================

import type { SupportedLocale } from '@/stores/onboardingStore'

export type NumeralsPreference = 'western' | 'eastern'

/** Map Western digit → Eastern Arabic (U+0660..U+0669). */
const WESTERN_TO_EASTERN: Record<string, string> = {
  '0': '٠', '1': '١', '2': '٢', '3': '٣', '4': '٤',
  '5': '٥', '6': '٦', '7': '٧', '8': '٨', '9': '٩',
}

/** Default numerals preference for a locale. */
export function inferNumeralsPreference(locale: SupportedLocale): NumeralsPreference {
  // Arab-sector convention: Eastern Arabic digits on student materials
  // unless the student overrides. Hebrew and English default to Western.
  return locale === 'ar' ? 'eastern' : 'western'
}

/**
 * Replace Western digits (0-9) with Eastern Arabic digits (٠-٩) in the
 * text portions of an HTML string. Tag names and attribute values are
 * preserved — otherwise we'd corrupt class names like "base3" that KaTeX
 * emits, or the `1em` unit in inline styles.
 *
 * This is a visual-only transform. Form inputs, CAS round-trips, and
 * analytics pipelines see the original Western digits.
 */
export function toEasternNumerals(html: string): string {
  if (!html) return html

  // Split by tag boundaries; swap only in the text segments.
  // Regex matches either a tag (<…>) or a non-tag run.
  return html.replace(/<[^>]*>|[^<]+/g, (segment) => {
    if (segment.startsWith('<'))
      return segment
    return convertDigits(segment, WESTERN_TO_EASTERN)
  })
}

/**
 * Pure digit-by-digit substitution on a plain string. Exposed for the
 * (rare) case where a caller wants to convert a variable value before
 * pasting it into UI copy (e.g. "answer: 42" → "answer: ٤٢").
 */
export function toEasternNumeralsText(text: string): string {
  return convertDigits(text, WESTERN_TO_EASTERN)
}

function convertDigits(text: string, map: Record<string, string>): string {
  let out = ''
  for (const ch of text) {
    out += map[ch] ?? ch
  }
  return out
}

// ── Spoken label lexicon (client fallback) ───────────────────────────────

interface SpokenLexicon {
  readonly plus: string
  readonly minus: string
  readonly equals: string
  readonly times: string
  readonly dividedBy: string
  readonly over: string
  readonly fractionOpen: string
  readonly fractionClose: string
  readonly sqrtOpen: string
  readonly sqrtClose: string
  readonly squared: string
  readonly cubed: string
  readonly toThePowerOf: string
  readonly subscript: string
  readonly openParen: string
  readonly closeParen: string
  readonly comma: string
  readonly point: string
  readonly infinity: string
  readonly lessEq: string
  readonly greaterEq: string
  readonly notEq: string
  readonly plusMinus: string
  readonly sin: string
  readonly cos: string
  readonly tan: string
  readonly log: string
  readonly ln: string
  readonly pi: string
  readonly theta: string
  readonly deltaUpper: string
  readonly deltaLower: string
}

const LEX_AR: SpokenLexicon = {
  plus: 'زائد',
  minus: 'ناقص',
  equals: 'يساوي',
  times: 'ضرب',
  dividedBy: 'قسمة على',
  over: 'على',
  fractionOpen: 'الكسر',
  fractionClose: 'نهاية الكسر',
  sqrtOpen: 'الجذر التربيعي',
  sqrtClose: 'نهاية الجذر',
  squared: 'تربيع',
  cubed: 'تكعيب',
  toThePowerOf: 'أس',
  subscript: 'ذيل',
  openParen: 'فتح قوس',
  closeParen: 'غلق قوس',
  comma: 'فاصلة',
  point: 'فاصلة عشرية',
  infinity: 'ما لا نهاية',
  lessEq: 'أصغر من أو يساوي',
  greaterEq: 'أكبر من أو يساوي',
  notEq: 'لا يساوي',
  plusMinus: 'زائد أو ناقص',
  sin: 'جا',
  cos: 'جتا',
  tan: 'ظا',
  log: 'لو',
  ln: 'لن',
  pi: 'باي',
  theta: 'ثيتا',
  deltaUpper: 'دلتا كبيرة',
  deltaLower: 'دلتا',
}

const LEX_HE: SpokenLexicon = {
  plus: 'ועוד',
  minus: 'פחות',
  equals: 'שווה',
  times: 'כפול',
  dividedBy: 'חלקי',
  over: 'חלקי',
  fractionOpen: 'השבר',
  fractionClose: 'סוף השבר',
  sqrtOpen: 'שורש ריבועי של',
  sqrtClose: 'סוף השורש',
  squared: 'בריבוע',
  cubed: 'בשלישית',
  toThePowerOf: 'בחזקת',
  subscript: 'אינדקס',
  openParen: 'פתיחת סוגריים',
  closeParen: 'סגירת סוגריים',
  comma: 'פסיק',
  point: 'נקודה עשרונית',
  infinity: 'אינסוף',
  lessEq: 'קטן או שווה ל',
  greaterEq: 'גדול או שווה ל',
  notEq: 'שונה מ',
  plusMinus: 'פלוס מינוס',
  sin: 'סינוס',
  cos: 'קוסינוס',
  tan: 'טנגנס',
  log: 'לוג',
  ln: 'לן',
  pi: 'פאי',
  theta: 'תטא',
  deltaUpper: 'דלתא גדולה',
  deltaLower: 'דלתא',
}

const LEX_EN: SpokenLexicon = {
  plus: 'plus',
  minus: 'minus',
  equals: 'equals',
  times: 'times',
  dividedBy: 'divided by',
  over: 'over',
  fractionOpen: 'the fraction',
  fractionClose: 'end fraction',
  sqrtOpen: 'the square root of',
  sqrtClose: 'end root',
  squared: 'squared',
  cubed: 'cubed',
  toThePowerOf: 'to the power of',
  subscript: 'sub',
  openParen: 'open paren',
  closeParen: 'close paren',
  comma: 'comma',
  point: ' point ',
  infinity: 'infinity',
  lessEq: 'less than or equal to',
  greaterEq: 'greater than or equal to',
  notEq: 'not equal to',
  plusMinus: 'plus or minus',
  sin: 'sine',
  cos: 'cosine',
  tan: 'tangent',
  log: 'log',
  ln: 'natural log',
  pi: 'pi',
  theta: 'theta',
  deltaUpper: 'capital delta',
  deltaLower: 'delta',
}

function resolveLex(locale: SupportedLocale): SpokenLexicon {
  if (locale === 'ar') return LEX_AR
  if (locale === 'he') return LEX_HE
  return LEX_EN
}

/**
 * Build a screen-reader-friendly label for a LaTeX expression in the
 * given locale. Mirrors MathAriaLabels.cs — keeps the two in sync for
 * offline / client-rendered cases. Server-emitted labels should be
 * preferred when present (they have the authoritative lexicon).
 */
export function buildAriaLabel(latex: string, locale: SupportedLocale): string {
  if (!latex) return ''
  const lex = resolveLex(locale)
  const tokens = tokenize(latex)
  return normalize(render(tokens, 0, lex).text)
}

interface RenderFrame { text: string; nextIndex: number }

function tokenize(latex: string): Token[] {
  const tokens: Token[] = []
  let i = 0
  while (i < latex.length) {
    const c = latex[i]
    if (c === '\\') {
      let j = i + 1
      if (j < latex.length && !isLetter(latex[j])) {
        tokens.push({ kind: 'cmd', value: latex.substring(i, j + 1) })
        i = j + 1; continue
      }
      while (j < latex.length && isLetter(latex[j])) j++
      tokens.push({ kind: 'cmd', value: latex.substring(i, j) })
      i = j; continue
    }
    if (c === '{') { tokens.push({ kind: 'lbrace', value: '{' }); i++; continue }
    if (c === '}') { tokens.push({ kind: 'rbrace', value: '}' }); i++; continue }
    if (c === '^') { tokens.push({ kind: 'caret', value: '^' }); i++; continue }
    if (c === '_') { tokens.push({ kind: 'under', value: '_' }); i++; continue }
    let k = i
    while (k < latex.length && !'\\{}^_'.includes(latex[k])) k++
    tokens.push({ kind: 'text', value: latex.substring(i, k) })
    i = k
  }
  return tokens
}

type Token = { kind: 'cmd' | 'lbrace' | 'rbrace' | 'caret' | 'under' | 'text'; value: string }

function isLetter(ch: string): boolean {
  return /[A-Za-z]/.test(ch)
}

function render(tokens: Token[], start: number, lex: SpokenLexicon, stopAtBrace = false): RenderFrame {
  let text = ''
  let i = start
  while (i < tokens.length) {
    const t = tokens[i]
    if (stopAtBrace && t.kind === 'rbrace')
      return { text, nextIndex: i }
    switch (t.kind) {
      case 'cmd': {
        const r = renderCommand(tokens, i, lex)
        text += ` ${r.text} `; i = r.nextIndex; break
      }
      case 'caret': {
        const r = renderPowerOrSub(tokens, i + 1, lex, 'power'); text += ` ${r.text} `; i = r.nextIndex; break
      }
      case 'under': {
        const r = renderPowerOrSub(tokens, i + 1, lex, 'sub'); text += ` ${r.text} `; i = r.nextIndex; break
      }
      case 'lbrace':
      case 'rbrace':
        i++; break
      case 'text':
        text += speakText(t.value, lex); i++; break
    }
  }
  return { text, nextIndex: i }
}

function renderCommand(tokens: Token[], i: number, lex: SpokenLexicon): RenderFrame {
  const cmd = tokens[i].value
  switch (cmd) {
    case '\\frac': {
      const num = readGroup(tokens, i + 1)
      const den = readGroup(tokens, num.nextIndex)
      const numInner = render(num.group, 0, lex).text.trim()
      const denInner = render(den.group, 0, lex).text.trim()
      return { text: `${lex.fractionOpen} ${numInner} ${lex.over} ${denInner} ${lex.fractionClose}`, nextIndex: den.nextIndex }
    }
    case '\\sqrt': {
      const g = readGroup(tokens, i + 1)
      const inner = render(g.group, 0, lex).text.trim()
      return { text: `${lex.sqrtOpen} ${inner} ${lex.sqrtClose}`, nextIndex: g.nextIndex }
    }
    case '\\infty': return { text: lex.infinity, nextIndex: i + 1 }
    case '\\pm':    return { text: lex.plusMinus, nextIndex: i + 1 }
    case '\\leq': case '\\le': return { text: lex.lessEq, nextIndex: i + 1 }
    case '\\geq': case '\\ge': return { text: lex.greaterEq, nextIndex: i + 1 }
    case '\\neq': case '\\ne': return { text: lex.notEq, nextIndex: i + 1 }
    case '\\cdot': case '\\times': return { text: lex.times, nextIndex: i + 1 }
    case '\\div': return { text: lex.dividedBy, nextIndex: i + 1 }
    case '\\sin': return { text: lex.sin, nextIndex: i + 1 }
    case '\\cos': return { text: lex.cos, nextIndex: i + 1 }
    case '\\tan': return { text: lex.tan, nextIndex: i + 1 }
    case '\\log': return { text: lex.log, nextIndex: i + 1 }
    case '\\ln':  return { text: lex.ln, nextIndex: i + 1 }
    case '\\pi':  return { text: lex.pi, nextIndex: i + 1 }
    case '\\theta': return { text: lex.theta, nextIndex: i + 1 }
    case '\\Delta': return { text: lex.deltaUpper, nextIndex: i + 1 }
    case '\\delta': return { text: lex.deltaLower, nextIndex: i + 1 }
    default:
      // Unknown command — strip backslash so we don't speak backslashes.
      return { text: cmd.replace(/^\\/, ''), nextIndex: i + 1 }
  }
}

function renderPowerOrSub(tokens: Token[], i: number, lex: SpokenLexicon, mode: 'power' | 'sub'): RenderFrame {
  const g = readGroup(tokens, i)
  if (mode === 'power') {
    const literal = g.group.map(t => t.value).join('').trim()
    if (literal === '2') return { text: lex.squared, nextIndex: g.nextIndex }
    if (literal === '3') return { text: lex.cubed, nextIndex: g.nextIndex }
    const inner = render(g.group, 0, lex).text.trim()
    return { text: `${lex.toThePowerOf} ${inner}`, nextIndex: g.nextIndex }
  }
  const inner = render(g.group, 0, lex).text.trim()
  return { text: `${lex.subscript} ${inner}`, nextIndex: g.nextIndex }
}

function readGroup(tokens: Token[], i: number): { group: Token[]; nextIndex: number } {
  if (i >= tokens.length) return { group: [], nextIndex: i }
  if (tokens[i].kind !== 'lbrace') {
    // Single-atom: if this is a text token longer than one char, only take
    // the first char — LaTeX rule for un-braced sub/super-scripts.
    if (tokens[i].kind === 'text' && tokens[i].value.length > 1) {
      const head = tokens[i].value[0]
      const tail = tokens[i].value.substring(1)
      tokens[i] = { kind: 'text', value: tail }
      return { group: [{ kind: 'text', value: head }], nextIndex: i }
    }
    return { group: [tokens[i]], nextIndex: i + 1 }
  }
  let depth = 1
  let j = i + 1
  const group: Token[] = []
  while (j < tokens.length && depth > 0) {
    const tk = tokens[j]
    if (tk.kind === 'lbrace') depth++
    else if (tk.kind === 'rbrace') { depth--; if (depth === 0) break }
    group.push(tk); j++
  }
  return { group, nextIndex: j + 1 }
}

function speakText(text: string, lex: SpokenLexicon): string {
  let out = ''
  for (const ch of text) {
    switch (ch) {
      case '+': out += ` ${lex.plus} `; break
      case '-': out += ` ${lex.minus} `; break
      case '=': out += ` ${lex.equals} `; break
      case '(': out += ` ${lex.openParen} `; break
      case ')': out += ` ${lex.closeParen} `; break
      case ',': out += ` ${lex.comma} `; break
      case '.': out += lex.point; break
      case '×': out += ` ${lex.times} `; break
      case '÷': out += ` ${lex.dividedBy} `; break
      case '≤': out += ` ${lex.lessEq} `; break
      case '≥': out += ` ${lex.greaterEq} `; break
      case '≠': out += ` ${lex.notEq} `; break
      case '∞': out += ` ${lex.infinity} `; break
      case ' ': out += ' '; break
      default: out += ch; break
    }
  }
  return out
}

function normalize(s: string): string {
  return s.split(/\s+/).filter(Boolean).join(' ')
}
