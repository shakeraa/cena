// =============================================================================
// Cena E2E flow — NATS bus probe (core-subscribe, no external deps)
//
// Purpose: let a spec assert that a specific event landed on the NATS bus
// within a timeout window, without adding an npm dependency. Uses a raw
// TCP socket against the dev-stack's cena-nats container (port 4222) and
// speaks the NATS core text protocol directly (documented at
// https://docs.nats.io/reference/reference-protocols/nats-protocol).
//
// Why raw TCP instead of `nats`/`@nats-io/nats-core`: the SPA's node_modules
// tree is a mixed pnpm/npm layout where `npm install` currently corrupts
// sibling packages. Shipping a devDep alongside the test infra would be a
// separate house-keeping task. Core NATS protocol is ~10 verbs and the probe
// needs 3 of them (CONNECT / SUB / MSG/HMSG) — cheaper to implement than to
// untangle the lockfiles.
//
// JetStream not required: we're asserting that an event was PUBLISHED on a
// subject. Core subscribers receive any published message on that subject
// regardless of whether JetStream is persisting it too. Persistence is a
// separate guarantee and has its own tests.
// =============================================================================

import { createConnection, type Socket } from 'node:net'
import { randomUUID } from 'node:crypto'

const NATS_HOST = process.env.NATS_HOST ?? 'localhost'
const NATS_PORT = Number(process.env.NATS_PORT ?? '4222')
// Dev stack uses subject-ACL auth (see docker/nats/nats-dev.conf). The
// `cena_api_user` account has subscribe permission on `cena.events.>` —
// exactly what the bus-probe needs for every test.
const NATS_USER = process.env.NATS_USER ?? 'cena_api_user'
const NATS_PASS = process.env.NATS_PASS ?? 'dev_api_pass'

export interface BusEnvelope {
  subject: string
  headers: Record<string, string>
  payload: string
  /** Parsed JSON payload, or null when the payload is not JSON. */
  json: Record<string, unknown> | null
}

export interface WaitForOptions {
  /** Subject (or wildcard) to subscribe to. e.g. `cena.events.student.*.onboarded`. */
  subject: string
  /**
   * Optional header filter. The probe collects messages matching `subject` and
   * returns the first one where every header in this object matches. Use when
   * multiple events fire on the same wildcard subject and you need to pick
   * one — e.g. filter by `tenant_id` so parallel workers don't cross-match.
   */
  requireHeader?: Record<string, string>
  /** Default 5000ms, matching the E2E boundary assertion window. */
  timeoutMs?: number
}

export interface BusProbe {
  waitFor(opts: WaitForOptions): Promise<BusEnvelope>
  assertNone(opts: { subject: string; timeoutMs?: number }): Promise<void>
  close(): Promise<void>
}

/**
 * Connect a bus probe to the dev-stack NATS server and return the handle.
 * Caller is responsible for `close()` in afterEach / test teardown.
 */
export async function createBusProbe(): Promise<BusProbe> {
  const socket = createConnection({ host: NATS_HOST, port: NATS_PORT })
  const state = new ProbeState(socket)
  await state.handshake()
  return {
    waitFor: opts => state.waitFor(opts),
    assertNone: opts => state.assertNone(opts),
    close: () => state.close(),
  }
}

// ── Internals ──
//
// We hold a single rolling buffer of bytes from the socket, split on `\r\n`
// between NATS verbs, and feed each verb into the `subscription` routers.
// Subscriptions are identified by incrementing sid. Each waitFor creates a
// fresh sid and resolves on the first matching MSG/HMSG.

type MessageListener = (msg: BusEnvelope) => void

class ProbeState {
  private buffer = Buffer.alloc(0)
  private nextSid = 1
  private listeners = new Map<number, MessageListener>()
  private pendingMsgHeader: { sid: number; subject: string; size: number; headerSize: number } | null = null
  private handshakeDone = false
  private handshakeResolve: (() => void) | null = null
  private closed = false

  constructor(private readonly socket: Socket) {
    socket.on('data', chunk => this.onData(chunk))
    socket.on('error', err => {
      if (!this.closed) {
        // eslint-disable-next-line no-console
        console.error('[bus-probe] socket error', err.message)
      }
    })
  }

  async handshake(): Promise<void> {
    await new Promise<void>((resolve, reject) => {
      this.handshakeResolve = resolve
      this.socket.once('error', reject)
      this.socket.once('connect', () => {
        const connectMsg = JSON.stringify({
          verbose: false,
          pedantic: false,
          tls_required: false,
          user: NATS_USER,
          pass: NATS_PASS,
          name: `cena-e2e-bus-probe-${randomUUID().slice(0, 8)}`,
          lang: 'node',
          version: '1.0.0',
          protocol: 1,
          headers: true,
          no_responders: false,
        })
        this.socket.write(`CONNECT ${connectMsg}\r\nPING\r\n`)
      })
    })
  }

  async waitFor(opts: WaitForOptions): Promise<BusEnvelope> {
    const timeout = opts.timeoutMs ?? 5000
    const sid = this.nextSid++
    return new Promise<BusEnvelope>((resolve, reject) => {
      const timer = setTimeout(() => {
        this.listeners.delete(sid)
        this.unsubscribe(sid)
        reject(new Error(
          `[bus-probe] Timed out after ${timeout}ms waiting for subject=${opts.subject}` +
          (opts.requireHeader ? ` requireHeader=${JSON.stringify(opts.requireHeader)}` : ''),
        ))
      }, timeout)

      this.listeners.set(sid, msg => {
        if (opts.requireHeader) {
          for (const [name, value] of Object.entries(opts.requireHeader)) {
            if (msg.headers[name] !== value)
              return
          }
        }
        clearTimeout(timer)
        this.listeners.delete(sid)
        this.unsubscribe(sid)
        resolve(msg)
      })

      this.socket.write(`SUB ${opts.subject} ${sid}\r\n`)
    })
  }

  async assertNone(opts: { subject: string; timeoutMs?: number }): Promise<void> {
    const timeout = opts.timeoutMs ?? 1000
    const sid = this.nextSid++
    return new Promise<void>((resolve, reject) => {
      const timer = setTimeout(() => {
        this.listeners.delete(sid)
        this.unsubscribe(sid)
        resolve()
      }, timeout)
      this.listeners.set(sid, msg => {
        clearTimeout(timer)
        this.listeners.delete(sid)
        this.unsubscribe(sid)
        reject(new Error(`[bus-probe] Unexpected message on ${opts.subject}: ${msg.payload}`))
      })
      this.socket.write(`SUB ${opts.subject} ${sid}\r\n`)
    })
  }

  async close(): Promise<void> {
    this.closed = true
    await new Promise<void>(resolve => this.socket.end(() => resolve()))
  }

  private unsubscribe(sid: number): void {
    if (this.closed)
      return
    try {
      this.socket.write(`UNSUB ${sid}\r\n`)
    }
    catch {
      // socket already gone — nothing to do
    }
  }

  private onData(chunk: Buffer): void {
    this.buffer = Buffer.concat([this.buffer, chunk])
    this.drain()
  }

  private drain(): void {
    // Two-state parser: reading verb lines vs. reading MSG/HMSG payloads.
    for (;;) {
      if (this.pendingMsgHeader) {
        const { subject, sid, size, headerSize } = this.pendingMsgHeader
        // Full frame = headerSize bytes of headers + size bytes of payload + trailing \r\n
        const needed = size + 2 // NATS appends \r\n after payload
        if (this.buffer.length < needed)
          return
        const frame = this.buffer.subarray(0, size)
        this.buffer = this.buffer.subarray(needed)
        const headers: Record<string, string> = {}
        let payloadStart = 0
        if (headerSize > 0) {
          const headerBlock = frame.subarray(0, headerSize).toString('utf8')
          // First line is `NATS/1.0\r\n`, then `Key: Value\r\n`, then `\r\n\r\n`
          const lines = headerBlock.split('\r\n')
          for (let i = 1; i < lines.length; i++) {
            const line = lines[i]
            if (!line)
              continue
            const idx = line.indexOf(':')
            if (idx > 0) {
              const name = line.slice(0, idx).trim()
              const value = line.slice(idx + 1).trim()
              headers[name] = value
            }
          }
          payloadStart = headerSize
        }
        const payload = frame.subarray(payloadStart).toString('utf8')
        let json: Record<string, unknown> | null = null
        try {
          json = JSON.parse(payload) as Record<string, unknown>
        }
        catch {
          json = null
        }
        const listener = this.listeners.get(sid)
        if (listener)
          listener({ subject, headers, payload, json })
        this.pendingMsgHeader = null
        continue
      }

      const crlf = this.buffer.indexOf('\r\n')
      if (crlf < 0)
        return
      const line = this.buffer.subarray(0, crlf).toString('utf8')
      this.buffer = this.buffer.subarray(crlf + 2)
      this.handleVerb(line)
    }
  }

  private handleVerb(line: string): void {
    if (line.startsWith('INFO '))
      return
    if (line === 'PING') {
      this.socket.write('PONG\r\n')
      return
    }
    if (line === 'PONG') {
      if (!this.handshakeDone) {
        this.handshakeDone = true
        this.handshakeResolve?.()
      }
      return
    }
    if (line === '+OK')
      return
    if (line.startsWith('-ERR ')) {
      // eslint-disable-next-line no-console
      console.error('[bus-probe] server error:', line)
      return
    }
    if (line.startsWith('MSG ')) {
      // MSG <subject> <sid> [reply] <size>
      const parts = line.split(' ')
      const subject = parts[1]
      const sid = Number(parts[2])
      const size = Number(parts[parts.length - 1])
      this.pendingMsgHeader = { subject, sid, size, headerSize: 0 }
      return
    }
    if (line.startsWith('HMSG ')) {
      // HMSG <subject> <sid> [reply] <hdrSize> <totalSize>
      const parts = line.split(' ')
      const subject = parts[1]
      const sid = Number(parts[2])
      const totalSize = Number(parts[parts.length - 1])
      const headerSize = Number(parts[parts.length - 2])
      this.pendingMsgHeader = { subject, sid, size: totalSize, headerSize }
    }
  }
}
