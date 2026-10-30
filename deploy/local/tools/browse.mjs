import { spawn } from 'node:child_process'
import { mkdtempSync, readFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

const steps = JSON.parse(readFileSync(process.argv[2], 'utf8'))
const chromePath = process.env.CHROME_PATH ?? 'C:/Program Files/Google/Chrome/Application/chrome.exe'
const port = 9400 + Math.floor(Math.random() * 400)
const profile = mkdtempSync(join(tmpdir(), 'reapseek-browse-'))
const chrome = spawn(chromePath, [
  '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
  `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, '--window-size=1280,900', 'about:blank',
], { stdio: 'ignore' })

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
let targets
for (let i = 0; i < 75 && !targets; i++) {
  try { targets = await (await fetch(`http://127.0.0.1:${port}/json`)).json() } catch { await sleep(200) }
}
const page = targets.find((t) => t.type === 'page')
const ws = new WebSocket(page.webSocketDebuggerUrl)
await new Promise((r) => ws.addEventListener('open', r))

let id = 0
const pending = new Map()
const errors = []
const requests = []
ws.addEventListener('message', (e) => {
  const msg = JSON.parse(e.data)
  if (msg.id && pending.has(msg.id)) {
    pending.get(msg.id)(msg)
    pending.delete(msg.id)
  } else if (msg.method === 'Runtime.exceptionThrown') {
    errors.push(msg.params.exceptionDetails.exception?.description ?? msg.params.exceptionDetails.text)
  } else if (msg.method === 'Runtime.consoleAPICalled' && msg.params.type === 'error') {
    errors.push(msg.params.args.map((a) => a.value ?? a.description).join(' '))
  } else if (msg.method === 'Network.requestWillBeSent') {
    requests.push({ url: msg.params.request.url, method: msg.params.request.method })
  }
})
const send = (method, params = {}) => new Promise((r) => {
  const i = ++id
  pending.set(i, r)
  ws.send(JSON.stringify({ id: i, method, params }))
})
const evaluate = async (expression) => {
  const res = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true })
  if (res.error) throw new Error(res.error.message)
  if (res.result?.exceptionDetails) throw new Error(res.result.exceptionDetails.exception?.description ?? 'evaluation failed')
  return res.result?.result?.value
}
const waitFor = async (expression, timeout = 15000) => {
  const deadline = Date.now() + timeout
  while (Date.now() < deadline) {
    if (await evaluate(`!!(${expression})`).catch(() => false)) return true
    await sleep(200)
  }
  throw new Error(`Timed out waiting for ${expression}`)
}

await send('Page.enable')
await send('Runtime.enable')
await send('Network.enable')

const results = {}
let failure = null
try {
  for (const step of steps) {
    if (step.cookie) await send('Network.setCookie', { name: step.cookie.name, value: step.cookie.value, domain: step.cookie.domain ?? 'localhost', path: '/', httpOnly: true })
    if (step.clearCookies) await send('Network.clearBrowserCookies')
    if (step.goto) {
      await send('Page.navigate', { url: step.goto })
      await waitFor("document.readyState === 'complete'")
    }
    if (step.waitFor) await waitFor(step.waitFor, step.timeout)
    if (step.click) {
      await waitFor(`document.querySelector(${JSON.stringify(step.click)})`)
      await evaluate(`document.querySelector(${JSON.stringify(step.click)}).click()`)
    }
    if (step.eval) results[step.name] = await evaluate(step.eval)
    if (step.sleep) await sleep(step.sleep)
  }
} catch (err) {
  failure = String(err)
  results.failedAt = await evaluate("location.href + ' | ' + (document.body ? document.body.innerText.slice(0, 500) : '')").catch((e) => String(e))
}

console.log(JSON.stringify({ failure, errors, results, requests: requests.filter((r) => !r.url.startsWith('data:')).map((r) => `${r.method} ${r.url}`) }))
ws.close()
chrome.kill()
await sleep(500)
try { rmSync(profile, { recursive: true, force: true }) } catch {}
process.exit(0)
