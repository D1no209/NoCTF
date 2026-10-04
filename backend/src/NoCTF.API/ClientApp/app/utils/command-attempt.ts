/** A retry reuses its nonce; a completed operation or changed input starts a new intent. Memory only. */
export function createCommandAttempt() {
  let input: string | null = null
  let key: string | null = null
  return {
    headers(payload: unknown): Record<string, string> {
      const next = JSON.stringify(payload)
      if (next !== input || !key) { input = next; key = newRequestKey() }
      return { 'Idempotency-Key': key }
    },
    completed() { input = null; key = null },
  }
}

const uncertain = new Map<string, string>()
const requests = new WeakMap<Request, { fingerprint: string, key: string }>()

function newRequestKey(): string {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID()
  // getRandomValues is also available on LAN HTTP origins, unlike randomUUID/subtle.
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  bytes[6] = (bytes[6]! & 15) | 64
  bytes[8] = (bytes[8]! & 63) | 128
  const hex = Array.from(bytes, value => value.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

async function localFingerprint(value: string): Promise<string> {
  return fingerprintBytes(new TextEncoder().encode(value))
}

async function fingerprintBytes(bytes: Uint8Array<ArrayBuffer>): Promise<string> {
  if (crypto.subtle) {
    const hash = await crypto.subtle.digest('SHA-256', bytes)
    return Array.from(new Uint8Array(hash), x => x.toString(16).padStart(2, '0')).join('')
  }
  // Client-only equality hint, not a security decision. The server always checks SHA-256.
  let hash = 0x6c62272e07bb014262b821756295c58dn
  for (const byte of bytes) hash = BigInt.asUintN(128, (hash ^ BigInt(byte)) * 0x1000000000000000000013bn)
  return hash.toString(16)
}

/** Only an indeterminate response is retried as the same intent. Successful requests are never content-deduplicated. */
export async function prepareCommandRequest(request: Request, body: unknown): Promise<void> {
  if (['GET', 'HEAD', 'OPTIONS'].includes(request.method)) return
  const content = body instanceof FormData ? await Promise.all(Array.from(body.entries(), async ([name, value]) => [name,
    value instanceof File ? { name: value.name, length: value.size, type: value.type,
      digest: await fingerprintBytes(new Uint8Array(await value.arrayBuffer())) } : value,
  ])) : body
  const serialized = JSON.stringify(content, (_key, value) => {
    if (typeof File !== 'undefined' && value instanceof File)
      return { file: value.name, length: value.size, modified: value.lastModified, type: value.type }
    if (typeof FormData !== 'undefined' && value instanceof FormData) return Array.from(value.entries())
    return value
  })
  let actor = ''
  try {
    const token = request.headers.get('Authorization')?.replace(/^Bearer /i, '').split('.')[1]
    if (token) actor = JSON.parse(atob(token.replace(/-/g, '+').replace(/_/g, '/'))).sub ?? ''
  } catch { /* Server authentication, not this client-side retry hint, establishes identity. */ }
  const fingerprint = await localFingerprint(`${actor}:${request.method}:${request.url}:${serialized}`)
  const key = request.headers.get('Idempotency-Key') || uncertain.get(fingerprint) || newRequestKey()
  request.headers.set('Idempotency-Key', key)
  requests.set(request, { fingerprint, key })
}

export function observeCommandResponse(request: Request | undefined, status: number | undefined, failed = false): void {
  if (!request) return
  const state = requests.get(request)
  if (!state) return
  if (failed && (status === undefined || status >= 500 || status === 429 || status < 300))
    uncertain.set(state.fingerprint, state.key)
  else if (uncertain.get(state.fingerprint) === state.key)
    uncertain.delete(state.fingerprint)
}
