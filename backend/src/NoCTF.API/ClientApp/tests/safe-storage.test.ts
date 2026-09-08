import { describe, expect, test } from 'bun:test'
import { readFeatureSource as readFileSync } from './support/feature-source'
import { resolve } from 'node:path'
import { createSafeStorage, type StringStorage } from '../app/lib/safe-storage'

describe('safe storage', () => {
  test('reads and writes through available browser storage', () => {
    const values = new Map<string, string>()
    const nativeStorage: StringStorage = {
      getItem: key => values.get(key) ?? null,
      setItem: (key, value) => values.set(key, value),
    }
    const storage = createSafeStorage(() => nativeStorage)

    storage.setItem('notice', 'read')

    expect(values.get('notice')).toBe('read')
    expect(storage.getItem('notice')).toBe('read')
  })

  test('falls back to session memory when browser storage operations throw', () => {
    const blockedStorage: StringStorage = {
      getItem: () => { throw new Error('storage disabled') },
      setItem: () => { throw new Error('quota denied') },
    }
    const storage = createSafeStorage(() => blockedStorage)

    expect(() => storage.setItem('question', '3')).not.toThrow()
    expect(() => storage.getItem('question')).not.toThrow()
    expect(storage.getItem('question')).toBe('3')
  })

  test('falls back when access to the browser storage object itself throws', () => {
    const storage = createSafeStorage(() => { throw new Error('security error') })

    storage.setItem('notification', 'latest')

    expect(storage.getItem('notification')).toBe('latest')
  })

  test('notification and question read state avoid direct localStorage access', () => {
    const clientRoot = resolve(import.meta.dir, '..', 'app', 'composables')
    const notificationSource = readFileSync(resolve(clientRoot, 'useNotificationUnread.ts'), 'utf8')
    const questionSource = readFileSync(resolve(clientRoot, 'useCompetitionQuestionReadState.ts'), 'utf8')

    expect(notificationSource).toContain('safeLocalStorage.getItem')
    expect(notificationSource).toContain('safeLocalStorage.setItem')
    expect(questionSource).toContain('safeLocalStorage.getItem')
    expect(questionSource).toContain('safeLocalStorage.setItem')
    expect(notificationSource).not.toMatch(/\blocalStorage\.(?:getItem|setItem)/)
    expect(questionSource).not.toMatch(/\blocalStorage\.(?:getItem|setItem)/)
  })
})
