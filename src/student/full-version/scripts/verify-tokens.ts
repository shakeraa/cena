import { studentDark, studentLight } from '../src/plugins/vuetify/theme'

interface Expected {
  flow: Record<string, string>
  mastery: Record<string, string>
}

const expectedLight: Expected = {
  flow: {
    warming: '#1565C0',
    approaching: '#FF8F00',
    inFlow: '#FFB300',
    disrupted: '#1565C0',
    fatigued: 'transparent',
  },
  mastery: {
    novice: '#EF5350',
    learning: '#FFA726',
    proficient: '#66BB6A',
    mastered: '#42A5F5',
    expert: '#AB47BC',
  },
}

function compare(label: string, actual: Record<string, string>, expected: Record<string, string>) {
  const keys = Object.keys(expected)
  let allOk = true
  for (const key of keys) {
    const a = actual[key]
    const e = expected[key]
    const ok = a === e

    console.log(`  ${ok ? 'OK ' : 'FAIL'} ${label}.${key} = ${a}${ok ? '' : ` (expected ${e})`}`)
    if (!ok)
      allOk = false
  }

  return allOk
}

console.log('STU-W-01 token verification')
console.log('---')
console.log('light theme tokens:')

const lightFlowOk = compare('flow', studentLight.flow, expectedLight.flow)
const lightMasteryOk = compare('mastery', studentLight.mastery, expectedLight.mastery)

console.log('dark theme tokens (pre-computed variants):')
console.log('  (parity check — all 10 dark keys present)')

const darkFlowKeys = Object.keys(studentDark.flow)
const darkMasteryKeys = Object.keys(studentDark.mastery)

const darkParityOk
  = darkFlowKeys.length === 5
  && darkMasteryKeys.length === 5
  && darkFlowKeys.every(k => typeof (studentDark.flow as any)[k] === 'string')
  && darkMasteryKeys.every(k => typeof (studentDark.mastery as any)[k] === 'string')

console.log(`  ${darkParityOk ? 'OK  ' : 'FAIL'} dark tokens complete`)

const total = [lightFlowOk, lightMasteryOk, darkParityOk].every(Boolean)

console.log('---')
console.log(total ? 'PASS — 10 light tokens + 10 dark tokens verified' : 'FAIL — token drift detected')
process.exit(total ? 0 : 1)
