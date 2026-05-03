#!/usr/bin/env node
/* eslint-disable */
//
// Cena Multi-Agent Task Queue
// ============================
//
// Shared SQLite-backed task queue for coordinating work between multiple
// coding agents (Claude Code, Kimi Code, human operators, sub-agents).
//
// Single authoritative state lives in .agentdb/kimi-queue.db and is safe
// for multiple concurrent readers + writers via SQLite WAL journal mode
// and an atomic UPDATE ... WHERE status='pending' claim sequence.
//
// Protocol, coder instructions, and rules: .agentdb/QUEUE.md
//
// All subcommands are idempotent where sane and print machine-parseable
// JSON on stdout when called with --json. Otherwise they print a short
// human summary suitable for agent chat windows.
//
// This file does NOT use child_process or shell invocation. It only
// reads and writes a local SQLite database via better-sqlite3.
//

'use strict';

const path = require('path');
const fs = require('fs');
const crypto = require('crypto');

let Database;
try {
  Database = require('better-sqlite3');
} catch (err) {
  console.error(
    'better-sqlite3 is not installed. Run `npm install better-sqlite3` at the repo root.'
  );
  process.exit(4);
}

const DB_PATH = path.resolve(__dirname, 'kimi-queue.db');

// ---------------------------------------------------------------------------
// Database
// ---------------------------------------------------------------------------

const SCHEMA = `
CREATE TABLE IF NOT EXISTS tasks (
  id           TEXT PRIMARY KEY,
  title        TEXT NOT NULL,
  body         TEXT NOT NULL DEFAULT '',
  status       TEXT NOT NULL DEFAULT 'pending'
                 CHECK (status IN ('pending','in_progress','done','failed')),
  priority     TEXT NOT NULL DEFAULT 'normal'
                 CHECK (priority IN ('low','normal','high','critical')),
  assignee     TEXT,
  tags         TEXT NOT NULL DEFAULT '',
  worker       TEXT,
  result       TEXT,
  failure      TEXT,
  created_at   INTEGER NOT NULL,
  claimed_at   INTEGER,
  completed_at INTEGER,
  updated_at   INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_tasks_status   ON tasks(status);
CREATE INDEX IF NOT EXISTS idx_tasks_assignee ON tasks(assignee);
CREATE INDEX IF NOT EXISTS idx_tasks_priority ON tasks(priority);

CREATE TABLE IF NOT EXISTS task_events (
  id         INTEGER PRIMARY KEY AUTOINCREMENT,
  task_id    TEXT NOT NULL,
  event      TEXT NOT NULL,
  worker     TEXT,
  payload    TEXT,
  created_at INTEGER NOT NULL,
  FOREIGN KEY (task_id) REFERENCES tasks(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_events_task ON task_events(task_id);

-- Inter-agent messaging: worker <-> worker direct messages and broadcasts.
-- Messages are pull-based: the recipient runs recv to fetch unacked messages
-- and ack to mark them consumed.
CREATE TABLE IF NOT EXISTS messages (
  id             TEXT PRIMARY KEY,
  from_worker    TEXT NOT NULL,
  to_worker      TEXT,                   -- NULL = broadcast
  topic          TEXT,                   -- optional channel
  kind           TEXT NOT NULL DEFAULT 'note'
                   CHECK (kind IN ('note','status','question','answer','directive','ack')),
  subject        TEXT NOT NULL DEFAULT '',
  body           TEXT NOT NULL DEFAULT '',
  task_id        TEXT,                   -- optional link to a related task
  correlation_id TEXT,                   -- links question<->answer
  created_at     INTEGER NOT NULL,
  consumed_at    INTEGER,                -- NULL until the recipient acks it
  consumed_by    TEXT                    -- worker name that acked
);

CREATE INDEX IF NOT EXISTS idx_msg_to         ON messages(to_worker);
CREATE INDEX IF NOT EXISTS idx_msg_from       ON messages(from_worker);
CREATE INDEX IF NOT EXISTS idx_msg_topic      ON messages(topic);
CREATE INDEX IF NOT EXISTS idx_msg_consumed   ON messages(consumed_at);
CREATE INDEX IF NOT EXISTS idx_msg_correlation ON messages(correlation_id);

-- Worker registry: tracks every agent that has ever joined the bus and
-- when they were last seen. Used by the "workers" subcommand so any
-- participant can list who else is around.
CREATE TABLE IF NOT EXISTS workers (
  name           TEXT PRIMARY KEY,
  capabilities   TEXT NOT NULL DEFAULT '',
  status         TEXT NOT NULL DEFAULT 'unknown',
  handshake_id   TEXT,                    -- task id of the passing handshake
  first_seen     INTEGER NOT NULL,
  last_seen      INTEGER NOT NULL,
  metadata       TEXT NOT NULL DEFAULT ''
);
`;

function openDb() {
  const db = new Database(DB_PATH);
  db.pragma('journal_mode = WAL');
  db.pragma('foreign_keys = ON');
  db.pragma('busy_timeout = 2000');
  db.exec(SCHEMA);
  return db;
}

function now() {
  return Date.now();
}

function newId() {
  return 't_' + crypto.randomBytes(6).toString('hex');
}

function newMsgId() {
  return 'm_' + crypto.randomBytes(6).toString('hex');
}

function touchWorker(db, name, statusOverride) {
  const ts = now();
  const existing = db.prepare(`SELECT * FROM workers WHERE name = ?`).get(name);
  if (existing) {
    db.prepare(
      `UPDATE workers SET last_seen = ?, status = COALESCE(?, status) WHERE name = ?`
    ).run(ts, statusOverride || null, name);
  } else {
    db.prepare(
      `INSERT INTO workers (name, status, first_seen, last_seen) VALUES (?, ?, ?, ?)`
    ).run(name, statusOverride || 'active', ts, ts);
  }
}

function writeEvent(db, taskId, event, worker, payload) {
  db.prepare(
    `INSERT INTO task_events (task_id, event, worker, payload, created_at)
     VALUES (?, ?, ?, ?, ?)`
  ).run(taskId, event, worker || null, payload ? JSON.stringify(payload) : null, now());
}

// ---------------------------------------------------------------------------
// CLI parsing
// ---------------------------------------------------------------------------

function parseArgs(argv) {
  const args = { _: [] };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith('--')) {
      const key = a.slice(2);
      const next = argv[i + 1];
      if (next === undefined || next.startsWith('--')) {
        args[key] = true;
      } else {
        args[key] = next;
        i++;
      }
    } else {
      args._.push(a);
    }
  }
  return args;
}

function readBody(args) {
  if (args['body-file']) {
    return fs.readFileSync(path.resolve(args['body-file']), 'utf8');
  }
  if (args.body) return String(args.body);
  return '';
}

function readResult(args) {
  if (args['result-file']) {
    return fs.readFileSync(path.resolve(args['result-file']), 'utf8');
  }
  if (args.result) return String(args.result);
  return null;
}

function printTask(task, json) {
  if (json) {
    console.log(JSON.stringify(task, null, 2));
    return;
  }
  const age = formatAge(now() - task.created_at);
  console.log(
    `[${task.id}] ${task.status.padEnd(11)} ${task.priority.padEnd(8)} ` +
      `${task.assignee || '-'.padEnd(12)}  ${task.title}  (age ${age})`
  );
}

function formatAge(ms) {
  const s = Math.round(ms / 1000);
  if (s < 60) return `${s}s`;
  const m = Math.round(s / 60);
  if (m < 60) return `${m}m`;
  const h = Math.round(m / 60);
  if (h < 24) return `${h}h`;
  const d = Math.round(h / 24);
  return `${d}d`;
}

// ---------------------------------------------------------------------------
// Subcommand handlers
// ---------------------------------------------------------------------------

const handlers = {};

handlers.init = () => {
  openDb().close();
  console.log(`queue initialized at ${DB_PATH}`);
};

handlers.enqueue = (args) => {
  const title = args._[1];
  if (!title) fail('usage: enqueue <title> [--body ...]');
  const body = readBody(args);
  const priority = String(args.priority || 'normal');
  const assignee = args.assignee ? String(args.assignee) : null;
  const tags = args.tags ? String(args.tags) : '';
  const id = newId();
  const ts = now();

  const db = openDb();
  db.prepare(
    `INSERT INTO tasks (id, title, body, priority, assignee, tags, created_at, updated_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?)`
  ).run(id, title, body, priority, assignee, tags, ts, ts);
  writeEvent(db, id, 'enqueued', null, { title, priority, assignee });
  db.close();

  console.log(id);
};

handlers.next = (args) => {
  const assignee = args.assignee ? String(args.assignee) : null;
  const db = openDb();

  const priorityOrder = `CASE priority
    WHEN 'critical' THEN 0
    WHEN 'high'     THEN 1
    WHEN 'normal'   THEN 2
    WHEN 'low'      THEN 3
  END`;

  let row;
  if (assignee) {
    row = db
      .prepare(
        `SELECT * FROM tasks
         WHERE status = 'pending'
           AND (assignee IS NULL OR assignee = ?)
         ORDER BY
           CASE WHEN assignee = ? THEN 0 ELSE 1 END,
           ${priorityOrder},
           created_at ASC
         LIMIT 1`
      )
      .get(assignee, assignee);
  } else {
    row = db
      .prepare(
        `SELECT * FROM tasks
         WHERE status = 'pending'
         ORDER BY ${priorityOrder}, created_at ASC
         LIMIT 1`
      )
      .get();
  }
  db.close();

  if (!row) {
    if (args.json) console.log('null');
    else console.log('(queue empty)');
    return;
  }
  printTask(row, args.json);
};

handlers.claim = (args) => {
  const id = args._[1];
  const worker = args.worker && String(args.worker);
  if (!id || !worker) fail('usage: claim <id> --worker <name>');
  const db = openDb();

  const result = db
    .prepare(
      `UPDATE tasks
       SET status = 'in_progress',
           worker = ?,
           claimed_at = ?,
           updated_at = ?
       WHERE id = ? AND status = 'pending'`
    )
    .run(worker, now(), now(), id);

  if (result.changes === 0) {
    const row = db.prepare(`SELECT status, worker FROM tasks WHERE id = ?`).get(id);
    db.close();
    if (!row) exitNotFound(id);
    console.error(
      `conflict: task ${id} is ${row.status}${row.worker ? ' (held by ' + row.worker + ')' : ''}`
    );
    process.exit(3);
  }

  writeEvent(db, id, 'claimed', worker, null);
  const row = db.prepare(`SELECT * FROM tasks WHERE id = ?`).get(id);
  db.close();
  printTask(row, args.json);
};

handlers.complete = (args) => {
  const id = args._[1];
  const worker = args.worker && String(args.worker);
  if (!id || !worker) fail('usage: complete <id> --worker <name> [--result ...]');
  const result = readResult(args);
  const db = openDb();

  const row = db.prepare(`SELECT * FROM tasks WHERE id = ?`).get(id);
  if (!row) {
    db.close();
    exitNotFound(id);
  }
  if (row.status !== 'in_progress') {
    db.close();
    console.error(`conflict: task ${id} is ${row.status}, cannot complete`);
    process.exit(3);
  }
  if (row.worker !== worker) {
    db.close();
    console.error(`conflict: task ${id} is held by ${row.worker}, not ${worker}`);
    process.exit(3);
  }

  db.prepare(
    `UPDATE tasks
     SET status = 'done',
         result = ?,
         completed_at = ?,
         updated_at = ?
     WHERE id = ?`
  ).run(result, now(), now(), id);
  writeEvent(db, id, 'completed', worker, { result });
  db.close();
  console.log(`done: ${id}`);
};

handlers.fail = (args) => {
  const id = args._[1];
  const worker = args.worker && String(args.worker);
  const reason = args.reason && String(args.reason);
  if (!id || !worker || !reason) fail('usage: fail <id> --worker <name> --reason <text>');
  const db = openDb();

  const row = db.prepare(`SELECT * FROM tasks WHERE id = ?`).get(id);
  if (!row) {
    db.close();
    exitNotFound(id);
  }
  if (row.status !== 'in_progress') {
    db.close();
    console.error(`conflict: task ${id} is ${row.status}, cannot fail`);
    process.exit(3);
  }
  if (row.worker !== worker) {
    db.close();
    console.error(`conflict: task ${id} is held by ${row.worker}, not ${worker}`);
    process.exit(3);
  }

  db.prepare(
    `UPDATE tasks
     SET status = 'failed',
         failure = ?,
         completed_at = ?,
         updated_at = ?
     WHERE id = ?`
  ).run(reason, now(), now(), id);
  writeEvent(db, id, 'failed', worker, { reason });
  db.close();
  console.log(`failed: ${id}`);
};

handlers.release = (args) => {
  const id = args._[1];
  const worker = args.worker && String(args.worker);
  if (!id || !worker) fail('usage: release <id> --worker <name>');
  const db = openDb();

  const row = db.prepare(`SELECT * FROM tasks WHERE id = ?`).get(id);
  if (!row) {
    db.close();
    exitNotFound(id);
  }
  if (row.status !== 'in_progress') {
    db.close();
    console.error(`conflict: task ${id} is ${row.status}, cannot release`);
    process.exit(3);
  }
  if (row.worker !== worker) {
    db.close();
    console.error(`conflict: task ${id} is held by ${row.worker}, not ${worker}`);
    process.exit(3);
  }

  db.prepare(
    `UPDATE tasks
     SET status = 'pending',
         worker = NULL,
         claimed_at = NULL,
         updated_at = ?
     WHERE id = ?`
  ).run(now(), id);
  writeEvent(db, id, 'released', worker, null);
  db.close();
  console.log(`released: ${id}`);
};

handlers.list = (args) => {
  const status = args.status ? String(args.status) : 'pending';
  const assignee = args.assignee ? String(args.assignee) : null;
  const db = openDb();

  let rows;
  if (status === 'all') {
    rows = assignee
      ? db.prepare(`SELECT * FROM tasks WHERE assignee = ? ORDER BY created_at ASC`).all(assignee)
      : db.prepare(`SELECT * FROM tasks ORDER BY created_at ASC`).all();
  } else {
    rows = assignee
      ? db
          .prepare(
            `SELECT * FROM tasks WHERE status = ? AND assignee = ? ORDER BY created_at ASC`
          )
          .all(status, assignee)
      : db.prepare(`SELECT * FROM tasks WHERE status = ? ORDER BY created_at ASC`).all(status);
  }
  db.close();

  if (args.json) {
    console.log(JSON.stringify(rows, null, 2));
    return;
  }
  if (rows.length === 0) {
    console.log(`(no tasks with status='${status}'${assignee ? ' assignee=' + assignee : ''})`);
    return;
  }
  for (const row of rows) printTask(row, false);
};

handlers.show = (args) => {
  const id = args._[1];
  if (!id) fail('usage: show <id>');
  const db = openDb();
  const row = db.prepare(`SELECT * FROM tasks WHERE id = ?`).get(id);
  if (!row) {
    db.close();
    exitNotFound(id);
  }
  const events = db
    .prepare(`SELECT * FROM task_events WHERE task_id = ? ORDER BY id ASC`)
    .all(id);
  db.close();

  if (args.json) {
    console.log(JSON.stringify({ ...row, events }, null, 2));
    return;
  }
  console.log(`id:         ${row.id}`);
  console.log(`title:      ${row.title}`);
  console.log(`status:     ${row.status}`);
  console.log(`priority:   ${row.priority}`);
  console.log(`assignee:   ${row.assignee || '-'}`);
  console.log(`worker:     ${row.worker || '-'}`);
  console.log(`tags:       ${row.tags || '-'}`);
  console.log(`created:    ${new Date(row.created_at).toISOString()}`);
  if (row.claimed_at) console.log(`claimed:    ${new Date(row.claimed_at).toISOString()}`);
  if (row.completed_at) console.log(`completed:  ${new Date(row.completed_at).toISOString()}`);
  if (row.body) {
    console.log('---- body ----');
    console.log(row.body);
  }
  if (row.result) {
    console.log('---- result ----');
    console.log(row.result);
  }
  if (row.failure) {
    console.log('---- failure ----');
    console.log(row.failure);
  }
  if (events.length) {
    console.log('---- events ----');
    for (const e of events) {
      console.log(
        `${new Date(e.created_at).toISOString()}  ${e.event.padEnd(10)} ${e.worker || '-'}`
      );
    }
  }
};

handlers.update = (args) => {
  const id = args._[1];
  if (!id) fail('usage: update <id> [--title ...] [--body ...] [--priority ...] [--assignee ...]');
  const db = openDb();
  const row = db.prepare(`SELECT * FROM tasks WHERE id = ?`).get(id);
  if (!row) {
    db.close();
    exitNotFound(id);
  }
  if (row.status !== 'pending') {
    db.close();
    console.error(`conflict: task ${id} is ${row.status}, only pending tasks can be updated`);
    process.exit(3);
  }
  const fields = [];
  const values = [];
  if (args.title) {
    fields.push('title = ?');
    values.push(String(args.title));
  }
  if (args.body || args['body-file']) {
    fields.push('body = ?');
    values.push(readBody(args));
  }
  if (args.priority) {
    fields.push('priority = ?');
    values.push(String(args.priority));
  }
  if (args.assignee) {
    fields.push('assignee = ?');
    values.push(String(args.assignee));
  }
  if (args.tags) {
    fields.push('tags = ?');
    values.push(String(args.tags));
  }
  if (fields.length === 0) {
    db.close();
    fail('nothing to update');
  }
  fields.push('updated_at = ?');
  values.push(now());
  values.push(id);
  db.prepare(`UPDATE tasks SET ${fields.join(', ')} WHERE id = ?`).run(...values);
  writeEvent(db, id, 'updated', null, { fields });
  db.close();
  console.log(`updated: ${id}`);
};

handlers.delete = (args) => {
  const id = args._[1];
  if (!id) fail('usage: delete <id>');
  const db = openDb();
  const row = db.prepare(`SELECT status FROM tasks WHERE id = ?`).get(id);
  if (!row) {
    db.close();
    exitNotFound(id);
  }
  if (row.status === 'in_progress') {
    db.close();
    console.error(`conflict: task ${id} is in_progress, release or complete first`);
    process.exit(3);
  }
  db.prepare(`DELETE FROM tasks WHERE id = ?`).run(id);
  db.close();
  console.log(`deleted: ${id}`);
};

handlers.send = (args) => {
  // Send a message from one worker to another, or broadcast to a topic.
  //
  // Usage:
  //   send --from <worker> --to <worker> [--subject <text>] [--body ... | --body-file ...]
  //        [--kind note|status|question|answer|directive|ack]
  //        [--task <id>] [--correlation <id>]
  //   send --from <worker> --topic <name> [--body ...]    # broadcast to topic
  //
  // Notes:
  //   - "from" is required — messages are always signed
  //   - either --to OR --topic must be set (or both)
  //   - broadcasts go to every worker who subscribes to the topic (via recv --topic)
  const from = args.from && String(args.from);
  const to = args.to ? String(args.to) : null;
  const topic = args.topic ? String(args.topic) : null;
  if (!from || (!to && !topic)) {
    fail('usage: send --from <worker> (--to <worker> | --topic <name>) [--subject ...] [--body ...]');
  }
  const kind = String(args.kind || 'note');
  const subject = args.subject ? String(args.subject) : '';
  let body = '';
  if (args['body-file']) {
    body = fs.readFileSync(path.resolve(args['body-file']), 'utf8');
  } else if (args.body && args.body !== true) {
    body = String(args.body);
  }
  const taskId = args.task ? String(args.task) : null;
  const correlationId = args.correlation ? String(args.correlation) : null;

  const id = newMsgId();
  const ts = now();
  const db = openDb();
  touchWorker(db, from);
  if (to) touchWorker(db, to);
  db.prepare(
    `INSERT INTO messages
       (id, from_worker, to_worker, topic, kind, subject, body, task_id, correlation_id, created_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
  ).run(id, from, to, topic, kind, subject, body, taskId, correlationId, ts);
  db.close();

  if (args.json) {
    console.log(JSON.stringify({ id, from, to, topic, kind, subject, taskId, correlationId }, null, 2));
  } else {
    const target = to ? `to ${to}` : `to topic "${topic}"`;
    console.log(`sent ${id} ${target} (kind=${kind}${subject ? ', subject="' + subject + '"' : ''})`);
  }
};

handlers.recv = (args) => {
  // Pull unacked messages for a worker.
  //
  // Usage:
  //   recv --worker <name> [--topic <name>] [--kind ...] [--peek] [--since <ms>] [--json]
  //
  // Behavior:
  //   - Returns messages where to_worker = <worker> AND consumed_at IS NULL
  //   - Also returns messages where topic = <topic>  AND consumed_at IS NULL
  //     if --topic is passed
  //   - Without --peek, atomically marks them consumed before returning
  //   - With --peek, leaves them unconsumed (useful for inspecting without side effects)
  const worker = args.worker && String(args.worker);
  if (!worker) fail('usage: recv --worker <name> [--topic ...] [--peek] [--json]');
  const topic = args.topic ? String(args.topic) : null;
  const kind = args.kind ? String(args.kind) : null;
  const peek = !!args.peek;
  const since = args.since ? Number(args.since) : 0;

  const db = openDb();
  touchWorker(db, worker);

  // Build the query: direct OR topic, unconsumed, optional kind filter, optional since.
  const params = [];
  const parts = [];

  // direct
  parts.push('(to_worker = ?)');
  params.push(worker);

  // topic
  if (topic) {
    parts.push('(topic = ?)');
    params.push(topic);
  }

  const whereParts = [`(${parts.join(' OR ')})`, 'consumed_at IS NULL'];
  if (kind) {
    whereParts.push('kind = ?');
    params.push(kind);
  }
  if (since) {
    whereParts.push('created_at >= ?');
    params.push(since);
  }
  const where = whereParts.join(' AND ');

  const rows = db
    .prepare(`SELECT * FROM messages WHERE ${where} ORDER BY created_at ASC`)
    .all(...params);

  if (!peek && rows.length) {
    const ids = rows.map((r) => r.id);
    const placeholders = ids.map(() => '?').join(',');
    db.prepare(
      `UPDATE messages SET consumed_at = ?, consumed_by = ? WHERE id IN (${placeholders})`
    ).run(now(), worker, ...ids);
  }
  db.close();

  if (args.json) {
    console.log(JSON.stringify(rows, null, 2));
    return;
  }
  if (rows.length === 0) {
    console.log(`(no messages for ${worker}${topic ? ' on topic ' + topic : ''})`);
    return;
  }
  for (const row of rows) {
    const age = formatAge(now() - row.created_at);
    const to = row.to_worker || (row.topic ? `#${row.topic}` : '(broadcast)');
    console.log(
      `[${row.id}] ${row.kind.padEnd(9)} ${row.from_worker} -> ${to}  (age ${age})` +
        (row.correlation_id ? `  corr=${row.correlation_id}` : '') +
        (row.task_id ? `  task=${row.task_id}` : '')
    );
    if (row.subject) console.log(`  subject: ${row.subject}`);
    if (row.body) console.log(`  body: ${row.body.split('\n').join('\n        ')}`);
  }
  if (peek) console.log(`(peek mode: ${rows.length} message(s) left unconsumed)`);
};

handlers.ack = (args) => {
  // Explicitly ack a specific message by id. Usually recv auto-acks, but
  // peek-then-ack is useful for "I read it, I understood it, mark it done".
  const id = args._[1];
  const worker = args.worker && String(args.worker);
  if (!id || !worker) fail('usage: ack <message-id> --worker <name>');
  const db = openDb();
  const row = db.prepare(`SELECT * FROM messages WHERE id = ?`).get(id);
  if (!row) {
    db.close();
    exitNotFound(id);
  }
  if (row.consumed_at) {
    db.close();
    console.error(`message ${id} already consumed by ${row.consumed_by} at ${new Date(row.consumed_at).toISOString()}`);
    process.exit(3);
  }
  db.prepare(
    `UPDATE messages SET consumed_at = ?, consumed_by = ? WHERE id = ?`
  ).run(now(), worker, id);
  db.close();
  console.log(`acked: ${id}`);
};

handlers.workers = (args) => {
  // List all known workers with last-seen timestamps.
  // Used by any agent to see who else is on the bus.
  const since = args.since ? Number(args.since) : 0;
  const activeOnly = !!args.active;
  const db = openDb();

  let rows;
  if (activeOnly) {
    const cutoff = now() - 5 * 60 * 1000; // 5 minutes
    rows = db
      .prepare(`SELECT * FROM workers WHERE last_seen >= ? ORDER BY last_seen DESC`)
      .all(cutoff);
  } else if (since) {
    rows = db
      .prepare(`SELECT * FROM workers WHERE last_seen >= ? ORDER BY last_seen DESC`)
      .all(since);
  } else {
    rows = db.prepare(`SELECT * FROM workers ORDER BY last_seen DESC`).all();
  }
  db.close();

  if (args.json) {
    console.log(JSON.stringify(rows, null, 2));
    return;
  }
  if (rows.length === 0) {
    console.log('(no workers registered)');
    return;
  }
  for (const row of rows) {
    const lastSeenAge = formatAge(now() - row.last_seen);
    console.log(
      `${row.name.padEnd(24)} ${row.status.padEnd(10)} last_seen ${lastSeenAge} ago` +
        (row.handshake_id ? `  handshake=${row.handshake_id}` : '')
    );
  }
};

handlers.heartbeat = (args) => {
  // Worker checks in. Updates last_seen and optional status.
  const worker = args.worker && String(args.worker);
  if (!worker) fail('usage: heartbeat --worker <name> [--status active|busy|idle|offline]');
  const status = args.status ? String(args.status) : 'active';
  const db = openDb();
  touchWorker(db, worker, status);
  db.close();
  console.log(`heartbeat: ${worker} (${status})`);
};

handlers.handshake = (args) => {
  // Enqueue a handshake task for a specific worker. The handshake is a
  // zero-side-effect claim/complete cycle that proves the worker can read
  // the protocol, use the CLI, and extract a per-handshake rotating phrase
  // from its own task body.
  //
  // Usage: handshake <worker-name>
  //
  // Advisory only: the CLI does not block real task claims based on
  // handshake state. The coordinator verifies by reading the result row.
  const worker = args._[1];
  if (!worker) fail('usage: handshake <worker-name>');

  // Rotating phrase: 8-byte hex token unique to this handshake.
  // The worker must echo this exact value back in its --result.
  const phrase = crypto.randomBytes(8).toString('hex');

  const title = `HANDSHAKE: ${worker}`;
  const body = `## Goal

Prove you (worker="${worker}") can participate in the Cena multi-agent task queue. This is a no-op task — do not edit any files, do not run any build, do not commit anything. You only claim, read, and complete this task.

## Required reading BEFORE you claim

1. [CLAUDE.md](../CLAUDE.md) — project rules
2. [.agentdb/AGENT_CODER_INSTRUCTIONS.md](AGENT_CODER_INSTRUCTIONS.md) — multi-agent coordination protocol
3. [.agentdb/QUEUE.md](QUEUE.md) — task queue CLI + state machine

You must read all three end-to-end. If you skip this you will fail later tasks and waste everyone's time.

## Your handshake phrase

PHRASE: ${phrase}

(This phrase is unique to YOUR handshake task and will not appear in any other doc. The coordinator uses it to verify that you actually read *this* task body — not a generic memory of past handshakes.)

## Steps

1. Claim this task:

       node .agentdb/kimi-queue.js claim <task-id> --worker ${worker}

2. Show this task and confirm your worker name matches:

       node .agentdb/kimi-queue.js show <task-id>

3. Run a sanity check on your environment — verify you can require better-sqlite3:

       node -e "require('better-sqlite3'); console.log('ok')"

4. Complete this task with a --result string that contains ALL of the following, comma-separated:

   - phrase=<the PHRASE value from above>
   - worker=${worker}
   - node=<your node --version>
   - os=<your uname -s, e.g. Darwin / Linux>
   - files-read=3/3
   - ready=true

   Example:

       node .agentdb/kimi-queue.js complete <task-id> --worker ${worker} \\
         --result "phrase=${phrase},worker=${worker},node=v20.11.0,os=Darwin,files-read=3/3,ready=true"

## Definition of done

- [ ] You read CLAUDE.md, AGENT_CODER_INSTRUCTIONS.md, and QUEUE.md in full
- [ ] You claimed this task under the correct worker name ("${worker}")
- [ ] Your --result string contains the exact phrase "${phrase}"
- [ ] Your --result includes your Node version, OS, and files-read count
- [ ] The task transitions to status='done' after your complete call

## What happens next

The coordinator (claude-code main session) will read your result via:

    node .agentdb/kimi-queue.js show <task-id>

If the phrase matches and the fields are present, you are greenlit to claim real work. If not, the coordinator will fail-then-re-enqueue a fresh handshake and you start over.

## Rules (repeat from AGENT_CODER_INSTRUCTIONS)

- Do NOT claim any other task before this handshake is done.
- Do NOT work on a task you haven't claimed.
- Do NOT push to main.
- Do NOT modify .agentdb/*, .claude/*, or CLAUDE.md.
- If anything goes wrong, call fail with a reason. Do not silently drop.
`;

  const id = newId();
  const ts = now();
  const db = openDb();
  db.prepare(
    `INSERT INTO tasks (id, title, body, priority, assignee, tags, created_at, updated_at)
     VALUES (?, ?, ?, 'critical', ?, 'handshake,safe', ?, ?)`
  ).run(id, title, body, worker, ts, ts);
  writeEvent(db, id, 'enqueued', null, { handshake: true, worker, phrase });
  db.close();

  console.log(id);
  console.log(`phrase: ${phrase}`);
  console.log(`worker: ${worker}`);
  console.log('(the coordinator should remember this phrase to verify the result)');
};

handlers.verify = (args) => {
  // Verify a completed handshake matches its issued phrase.
  //
  // Usage: verify <task-id>
  //
  // Returns exit code 0 if all fields present and phrase matches,
  // 1 if task is not a handshake or not done, 3 if verification fails.
  const id = args._[1];
  if (!id) fail('usage: verify <task-id>');
  const db = openDb();
  const row = db.prepare(`SELECT * FROM tasks WHERE id = ?`).get(id);
  if (!row) {
    db.close();
    exitNotFound(id);
  }
  if (!row.tags || !row.tags.includes('handshake')) {
    db.close();
    console.error(`not a handshake task: ${id}`);
    process.exit(1);
  }
  if (row.status !== 'done') {
    db.close();
    console.error(`handshake ${id} is ${row.status}, not done`);
    process.exit(1);
  }
  // Extract expected phrase from the enqueue event
  const enqueueEvent = db
    .prepare(`SELECT payload FROM task_events WHERE task_id = ? AND event = 'enqueued'`)
    .get(id);
  db.close();

  if (!enqueueEvent || !enqueueEvent.payload) {
    console.error('enqueue event missing payload — cannot verify phrase');
    process.exit(3);
  }
  const expected = JSON.parse(enqueueEvent.payload);
  const expectedPhrase = expected.phrase;
  const expectedWorker = expected.worker;
  if (!expectedPhrase) {
    console.error('no phrase recorded in enqueue payload');
    process.exit(3);
  }

  const result = row.result || '';
  const checks = {
    phrase: result.includes(`phrase=${expectedPhrase}`),
    worker: result.includes(`worker=${expectedWorker}`),
    node: /node=v?\d/.test(result),
    os: /os=\w/.test(result),
    filesRead: result.includes('files-read=3/3'),
    ready: result.includes('ready=true'),
  };

  const allPassed = Object.values(checks).every((v) => v === true);

  if (args.json) {
    console.log(JSON.stringify({ id, worker: row.worker, allPassed, checks, result }, null, 2));
  } else {
    console.log(`handshake ${id}`);
    console.log(`  worker:       ${row.worker}`);
    console.log(`  expected:     ${expectedWorker}`);
    for (const [k, v] of Object.entries(checks)) {
      console.log(`  ${k.padEnd(12)} ${v ? 'OK' : 'MISSING'}`);
    }
    console.log(`  verdict:      ${allPassed ? 'PASS' : 'FAIL'}`);
  }
  if (!allPassed) process.exit(3);
};

handlers.stats = (args) => {
  const db = openDb();
  const byStatus = db
    .prepare(`SELECT status, COUNT(*) AS n FROM tasks GROUP BY status ORDER BY status`)
    .all();
  const byAssignee = db
    .prepare(
      `SELECT COALESCE(assignee, '(unassigned)') AS assignee, COUNT(*) AS n
       FROM tasks GROUP BY assignee ORDER BY assignee`
    )
    .all();
  const byWorker = db
    .prepare(
      `SELECT worker, COUNT(*) AS n
       FROM tasks WHERE worker IS NOT NULL GROUP BY worker ORDER BY worker`
    )
    .all();
  db.close();

  if (args.json) {
    console.log(JSON.stringify({ byStatus, byAssignee, byWorker }, null, 2));
    return;
  }
  console.log('by status:');
  for (const r of byStatus) console.log(`  ${r.status.padEnd(12)} ${r.n}`);
  console.log('by assignee:');
  for (const r of byAssignee) console.log(`  ${r.assignee.padEnd(18)} ${r.n}`);
  if (byWorker.length) {
    console.log('by worker:');
    for (const r of byWorker) console.log(`  ${r.worker.padEnd(18)} ${r.n}`);
  }
};

// ---------------------------------------------------------------------------
// Error helpers
// ---------------------------------------------------------------------------

function fail(msg) {
  console.error(msg);
  process.exit(1);
}
function exitNotFound(id) {
  console.error(`not found: ${id}`);
  process.exit(2);
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

function main() {
  const args = parseArgs(process.argv.slice(2));
  const name = args._[0];
  if (!name || args.help || name === 'help') {
    console.log(`kimi-queue — Cena multi-agent task queue

Database: ${DB_PATH}

Subcommands:
  init
  enqueue <title> [--body ...] [--body-file ...] [--assignee ...] [--priority ...] [--tags ...]
  next    [--assignee <name>] [--json]
  claim   <id> --worker <name>
  complete <id> --worker <name> [--result ...] [--result-file ...]
  fail    <id> --worker <name> --reason <text>
  release <id> --worker <name>
  list    [--status pending|in_progress|done|failed|all] [--assignee <name>] [--json]
  show    <id> [--json]
  update  <id> [--title ...] [--body ...] [--body-file ...] [--priority ...] [--assignee ...]
  delete  <id>
  stats   [--json]
  handshake <worker-name>         # enqueue a handshake task for a new worker
  verify <task-id>                # verify a completed handshake matches its phrase

Inter-agent messaging:
  send    --from <w> (--to <w> | --topic <t>) [--subject ...] [--body ... | --body-file ...]
          [--kind note|status|question|answer|directive|ack] [--task <id>] [--correlation <id>]
  recv    --worker <name> [--topic <t>] [--kind ...] [--peek] [--since <ms>] [--json]
  ack     <message-id> --worker <name>
  workers [--active] [--since <ms>] [--json]
  heartbeat --worker <name> [--status active|busy|idle|offline]

See .agentdb/QUEUE.md for the full protocol and agent coordination rules.
`);
    return;
  }
  const fn = handlers[name];
  if (!fn) fail(`unknown subcommand: ${name}`);
  fn(args);
}

main();
