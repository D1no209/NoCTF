export interface ResultObservation {
  matchId: string
  matchActive: boolean
  matchRevision: number
  matchEnded: boolean
  roundId: string | null
  roundActive: boolean
  roundRevision: number
  roundEnded: boolean
  defer: boolean
}
export interface ResultObservationState {
  activeMatch: boolean
  activeRounds: string[]
  matchRevision: number
  roundRevisions: Record<string, number>
}
export type ResultPrompt = 'Match' | 'Round'
export const emptyResultObservation = (): ResultObservationState => ({ activeMatch: false, activeRounds: [], matchRevision: 0, roundRevisions: {} })

/** Clock revisions and notifications do not enter this policy; only an authoritative workspace does. */
export function observeResult(previous: ResultObservationState, input: ResultObservation) {
  const state: ResultObservationState = { ...previous, activeRounds: [...previous.activeRounds], roundRevisions: { ...previous.roundRevisions } }
  if (input.matchActive) state.activeMatch = true
  if (input.roundActive && input.roundId && !state.activeRounds.includes(input.roundId)) state.activeRounds.push(input.roundId)
  const matchNew = input.matchEnded && input.matchRevision > state.matchRevision
  const roundNew = input.roundEnded && input.roundId && input.roundRevision > (state.roundRevisions[input.roundId] ?? 0)
  let prompt: ResultPrompt | null = null
  if (!input.defer) {
    if (matchNew && state.activeMatch) prompt = 'Match'
    else if (roundNew && state.activeRounds.includes(input.roundId!)) prompt = 'Round'
  }
  // A historical first visit establishes a baseline without prompting. An active visit deferred by
  // editing or another dialog must not consume the result until it can actually be shown.
  if (matchNew && (!state.activeMatch || !input.defer)) state.matchRevision = input.matchRevision
  if (roundNew && (!state.activeRounds.includes(input.roundId!) || !input.defer)) state.roundRevisions[input.roundId!] = input.roundRevision
  return { state, prompt }
}

export interface ResultPromptStore { observe(key: string, input: ResultObservation): Promise<ResultPrompt | null> }

export function indexedDbResultPromptStore(factory: IDBFactory = indexedDB): ResultPromptStore {
  let opened: Promise<IDBDatabase> | null = null
  function database() {
    opened ??= new Promise((resolve, reject) => {
      const request = factory.open('NoCTF.LiveSolo.ResultPrompts', 1)
      request.onupgradeneeded = () => { request.result.createObjectStore('observations') }
      request.onerror = () => reject(request.error)
      request.onblocked = () => reject(new Error('Result prompt storage blocked'))
      request.onsuccess = () => {
        request.result.onversionchange = () => { request.result.close(); opened = null }
        resolve(request.result)
      }
    })
    return opened
  }
  return {
    async observe(key, input) {
      const db = await database()
      return new Promise<ResultPrompt | null>((resolve, reject) => {
        // A single read/write transaction serializes competing tabs, including the active marker.
        const transaction = db.transaction('observations', 'readwrite')
        const records = transaction.objectStore('observations'), request = records.get(key)
        let prompt: ResultPrompt | null = null
        request.onsuccess = () => {
          const decision = observeResult(request.result ?? emptyResultObservation(), input)
          prompt = decision.prompt; records.put(decision.state, key)
        }
        transaction.oncomplete = () => resolve(prompt)
        transaction.onerror = () => reject(transaction.error)
        transaction.onabort = () => reject(transaction.error)
      })
    },
  }
}
