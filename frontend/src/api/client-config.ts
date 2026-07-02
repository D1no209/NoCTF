import type { ClientOptions, Config } from './generated/client'
import type { ClientOptions as GeneratedClientOptions } from './generated/types.gen'

const DEFAULT_REQUEST_TIMEOUT_MS = 12_000

function timeoutFetch(baseFetch: typeof fetch, timeoutMs = DEFAULT_REQUEST_TIMEOUT_MS): typeof fetch {
  return async (input, init) => {
    const controller = new AbortController()
    const timeout = window.setTimeout(() => controller.abort(), timeoutMs)
    const inputSignal = input instanceof Request ? input.signal : undefined
    const initSignal = init?.signal

    const abortFromParent = () => controller.abort()
    inputSignal?.addEventListener('abort', abortFromParent, { once: true })
    initSignal?.addEventListener('abort', abortFromParent, { once: true })

    try {
      let response: Response

      if (input instanceof Request) {
        response = await baseFetch(new Request(input, { signal: controller.signal }))
      } else {
        response = await baseFetch(input, { ...init, signal: controller.signal })
      }

      if (response.status === 401) {
        localStorage.removeItem('accessToken')
        localStorage.removeItem('authUser')

        if (!location.pathname.startsWith('/login')) {
          location.replace(`/login?redirect=${encodeURIComponent(location.pathname + location.search)}`)
        }
      }

      return response
    } finally {
      window.clearTimeout(timeout)
      inputSignal?.removeEventListener('abort', abortFromParent)
      initSignal?.removeEventListener('abort', abortFromParent)
    }
  }
}

export function createClientConfig(
  override?: Config<ClientOptions & GeneratedClientOptions>,
): Config<ClientOptions & GeneratedClientOptions> {
  const baseFetch = override?.fetch ?? globalThis.fetch

  return {
    ...override,
    baseUrl: override?.baseUrl ?? import.meta.env.VITE_API_BASE_URL ?? '',
    fetch: timeoutFetch(baseFetch),
  }
}
