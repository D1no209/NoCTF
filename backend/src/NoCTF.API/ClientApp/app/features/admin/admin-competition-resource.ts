import { shallowRef } from 'vue'
import { adminGetCompetition } from '../../api'
import type { NoCtfapiEndpointsAdministrationCompetitionsAdminCompetitionResponse as AdminCompetition } from '../../api'

type ReadResult = { data?: AdminCompetition, error?: unknown, request?: Request, response?: Response }

/** One client-only workspace snapshot; never persisted to browser storage. */
export function createAdminCompetitionResource(loadRemote: (id: string) => Promise<ReadResult>) {
  const data = shallowRef<AdminCompetition | null>(null)
  const fresh = shallowRef(false)
  const error = shallowRef<unknown>(null)
  let identity = '', revision = 0, last: ReadResult | null = null
  let pending: Promise<ReadResult> | null = null

  function reset(actor: string, id: string) {
    const key = `${actor}:${id}`
    if (identity === key) return
    identity = key; revision++; pending = null; last = null
    data.value = null; fresh.value = false; error.value = null
  }
  function invalidate() { revision++; pending = null; fresh.value = false }
  function clear(actor?: string, id?: string) { if (actor !== undefined && identity !== `${actor}:${id}`) return; identity = ''; invalidate(); data.value = null; last = null; error.value = null }

  function read(actor: string, id: string, force = false): Promise<ReadResult> {
    reset(actor, id)
    if (force && !pending) invalidate()
    if (fresh.value && last) return Promise.resolve(last)
    if (pending) return pending
    const generation = revision
    const task = loadRemote(id).then(result => {
      if (generation !== revision) return result
      if (!result.error && result.data) {
        data.value = result.data; fresh.value = true; error.value = null; last = result
      } else {
        fresh.value = false; error.value = result.error ?? new Error('Unavailable')
        if ([401, 403, 404].includes(result.response?.status ?? 0)) { data.value = null; last = null }
      }
      return result
    }).catch(cause => {
      if (generation === revision) { fresh.value = false; error.value = cause }
      throw cause
    }).finally(() => { if (pending === task) pending = null })
    pending = task
    return task
  }
  function accept(value: Partial<AdminCompetition>, actor?: string, id?: string) {
    if (!data.value || actor !== undefined && identity !== `${actor}:${id}`) return
    revision++; pending = null
    data.value = { ...data.value, ...value }
    if (last?.data) last = { ...last, data: data.value }
    fresh.value = true; error.value = null
  }
  return { data, fresh, error, read, reset, invalidate, clear, accept }
}

export const adminCompetitionResource = createAdminCompetitionResource(id =>
  adminGetCompetition({ path: { competitionId: id } }))
