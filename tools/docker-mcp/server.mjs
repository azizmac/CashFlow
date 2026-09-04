#!/usr/bin/env node
// Минимальный MCP-сервер (stdio, JSON-RPC по строкам) поверх локального Docker CLI.
// Зачем свой: каталожный сервер «docker» из Docker MCP Toolkit на Windows не передаёт аргументы в контейнер
// (docker-entrypoint.sh: exec: Permission denied), а этот запускает docker.exe хоста напрямую — без сокетов и контейнеров.
// Инструменты: docker (любые аргументы CLI), docker_compose (compose в корне репозитория), docker_overview (сводка).
// Без зависимостей: только node >= 18.
import { spawn } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const DOCKER = process.platform === "win32" ? "docker.exe" : "docker";
const MAX_OUTPUT = 60_000;

const tools = [
  {
    name: "docker",
    description:
      "Выполнить команду Docker CLI на хосте (ps, images, logs, inspect, build, run, stop, rm, rmi, tag, exec, system df …). " +
      "Передавайте аргументы массивом без слова docker, например [\"ps\",\"-a\"] или [\"logs\",\"--tail\",\"200\",\"cashflow-web-1\"].",
    inputSchema: {
      type: "object",
      properties: {
        args: { type: "array", items: { type: "string" }, description: "Аргументы docker CLI" },
        cwd: { type: "string", description: "Рабочая папка (по умолчанию корень репозитория)" },
        timeoutSec: { type: "number", description: "Таймаут, сек (по умолчанию 300)" },
      },
      required: ["args"],
    },
  },
  {
    name: "docker_compose",
    description:
      "Выполнить docker compose в корне репозитория (docker-compose.yml CashFlow): up -d --build, ps, logs --tail 100 web, down, restart web, config …",
    inputSchema: {
      type: "object",
      properties: {
        args: { type: "array", items: { type: "string" }, description: "Аргументы после «docker compose»" },
        timeoutSec: { type: "number", description: "Таймаут, сек (по умолчанию 900 — сборка образа бывает долгой)" },
      },
      required: ["args"],
    },
  },
  {
    name: "docker_overview",
    description: "Сводка: версия, запущенные и остановленные контейнеры, образы, тома, использование диска.",
    inputSchema: { type: "object", properties: {} },
  },
];

function run(args, { cwd = projectRoot, timeoutSec = 300 } = {}) {
  return new Promise((resolve) => {
    let out = "";
    let err = "";
    let child;
    try {
      child = spawn(DOCKER, args, { cwd, windowsHide: true, env: process.env });
    } catch (e) {
      return resolve({ code: -1, out: "", err: String(e) });
    }
    const timer = setTimeout(() => {
      err += `\n[timeout ${timeoutSec}s — процесс остановлен]`;
      child.kill();
    }, timeoutSec * 1000);
    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    child.stdout.on("data", (d) => (out += d));
    child.stderr.on("data", (d) => (err += d));
    child.on("error", (e) => { clearTimeout(timer); resolve({ code: -1, out, err: err + String(e) }); });
    child.on("close", (code) => { clearTimeout(timer); resolve({ code, out, err }); });
  });
}

function clip(s) {
  return s.length > MAX_OUTPUT ? s.slice(0, MAX_OUTPUT) + `\n… [обрезано, всего ${s.length} символов]` : s;
}

function format(cmd, r) {
  const parts = [`$ ${cmd}`];
  if (r.out.trim()) parts.push(r.out.trimEnd());
  if (r.err.trim()) parts.push(`[stderr]\n${r.err.trimEnd()}`);
  if (r.code !== 0) parts.push(`[exit code ${r.code}]`);
  return clip(parts.join("\n"));
}

async function callTool(name, a = {}) {
  if (name === "docker") {
    const args = Array.isArray(a.args) ? a.args.map(String) : [];
    if (args.length === 0) return { text: "Нужен массив args, например [\"ps\",\"-a\"]", isError: true };
    const r = await run(args, { cwd: a.cwd || projectRoot, timeoutSec: a.timeoutSec || 300 });
    return { text: format(`docker ${args.join(" ")}`, r), isError: r.code !== 0 };
  }
  if (name === "docker_compose") {
    const args = ["compose", ...(Array.isArray(a.args) ? a.args.map(String) : [])];
    const r = await run(args, { timeoutSec: a.timeoutSec || 900 });
    return { text: format(`docker ${args.join(" ")}`, r), isError: r.code !== 0 };
  }
  if (name === "docker_overview") {
    const sections = [
      ["version", ["version", "--format", "client {{.Client.Version}} / server {{.Server.Version}} ({{.Server.Os}}/{{.Server.Arch}})"]],
      ["containers", ["ps", "-a", "--format", "table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}"]],
      ["images", ["images", "--format", "table {{.Repository}}\t{{.Tag}}\t{{.ID}}\t{{.Size}}\t{{.CreatedSince}}"]],
      ["volumes", ["volume", "ls", "--format", "table {{.Name}}\t{{.Driver}}"]],
      ["disk", ["system", "df"]],
    ];
    const blocks = [];
    for (const [title, args] of sections) {
      const r = await run(args, { timeoutSec: 60 });
      blocks.push(`## ${title}\n${(r.out || r.err).trimEnd()}`);
    }
    return { text: clip(blocks.join("\n\n")), isError: false };
  }
  return { text: `Неизвестный инструмент: ${name}`, isError: true };
}

// ---- JSON-RPC over stdio (newline-delimited) ----
let buffer = "";
process.stdin.setEncoding("utf8");
process.stdin.on("data", (chunk) => {
  buffer += chunk;
  let idx;
  while ((idx = buffer.indexOf("\n")) >= 0) {
    const line = buffer.slice(0, idx).trim();
    buffer = buffer.slice(idx + 1);
    if (line) handle(line);
  }
});
let pending = 0;
let stdinClosed = false;
process.stdin.on("end", () => { stdinClosed = true; if (pending === 0) process.exit(0); });
function done() { pending--; if (stdinClosed && pending === 0) process.exit(0); }

function send(msg) {
  process.stdout.write(JSON.stringify(msg) + "\n");
}

async function handle(line) {
  let req;
  try { req = JSON.parse(line); } catch { return; }
  const { id, method, params } = req;
  const reply = (result) => id !== undefined && send({ jsonrpc: "2.0", id, result });
  const fail = (code, message) => id !== undefined && send({ jsonrpc: "2.0", id, error: { code, message } });

  switch (method) {
    case "initialize":
      return reply({
        protocolVersion: params?.protocolVersion || "2025-06-18",
        capabilities: { tools: {} },
        serverInfo: { name: "cashflow-docker", version: "0.1.0" },
        instructions: `Docker CLI хоста. Рабочая папка compose: ${projectRoot}. Контейнеры CashFlow: web (порт 8080) и db (PostgreSQL).`,
      });
    case "notifications/initialized":
    case "notifications/cancelled":
      return;
    case "ping":
      return reply({});
    case "tools/list":
      return reply({ tools });
    case "tools/call": {
      pending++;
      try {
        const r = await callTool(params?.name, params?.arguments || {});
        reply({ content: [{ type: "text", text: r.text }], isError: r.isError });
      } catch (e) {
        reply({ content: [{ type: "text", text: String(e?.stack || e) }], isError: true });
      } finally { done(); }
      return;
    }
    default:
      return fail(-32601, `Method not found: ${method}`);
  }
}
