import type { ClientOptions, Config } from './generated/client'
import type { ClientOptions as GeneratedClientOptions } from './generated/types.gen'

export function createClientConfig(
  override?: Config<ClientOptions & GeneratedClientOptions>,
): Config<ClientOptions & GeneratedClientOptions> {
  return {
    ...override,
    baseUrl: override?.baseUrl ?? import.meta.env.VITE_API_BASE_URL ?? '',
  }
}
