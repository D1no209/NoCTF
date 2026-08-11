export interface StringStorage {
  getItem(key: string): string | null
  setItem(key: string, value: string): void
}

export interface SafeStorage extends StringStorage {}

export function createSafeStorage(resolveStorage: () => StringStorage | null): SafeStorage {
  const memory = new Map<string, string>()

  return {
    getItem(key: string): string | null {
      try {
        const value = resolveStorage()?.getItem(key) ?? null
        if (value !== null) {
          memory.set(key, value)
          return value
        }
      }
      catch {
        // Browser storage can be unavailable in privacy modes or restricted contexts.
      }

      return memory.get(key) ?? null
    },

    setItem(key: string, value: string): void {
      memory.set(key, value)

      try {
        resolveStorage()?.setItem(key, value)
      }
      catch {
        // The in-memory copy keeps the current session usable when persistence fails.
      }
    },
  }
}

export const safeLocalStorage = createSafeStorage(() => {
  if (!import.meta.client) return null
  return localStorage
})
