export interface SavedAnswer { revision: string; selected: string[] }
export interface AnswerRequest { requestId: string; revision: string; selected: string[] }
export interface AnswerDraft {
  selected: string[]
  base: SavedAnswer
  remote: SavedAnswer
  dirty: boolean
  conflict: boolean
  saving: boolean
  pending: AnswerRequest | null
}
export function sameAnswer(left: readonly string[], right: readonly string[]) {
  return left.length === right.length && new Set(left).size === left.length && left.every(value => right.includes(value))
}
export function createAnswer(saved: SavedAnswer): AnswerDraft {
  return { selected: [...saved.selected], base: saved, remote: saved, dirty: false, conflict: false, saving: false, pending: null }
}
export function selectAnswer(draft: AnswerDraft, selected: string[]) {
  draft.selected = [...new Set(selected)]
  draft.dirty = !sameAnswer(draft.selected, draft.remote.selected)
}
export function acknowledgeAnswer(draft: AnswerDraft, request: AnswerRequest, saved: SavedAnswer) {
  draft.remote = saved; draft.base = saved; draft.saving = false; draft.pending = null
  draft.dirty = !sameAnswer(draft.selected, saved.selected)
  draft.conflict = draft.dirty && !sameAnswer(request.selected, saved.selected)
}
export function reconcileAnswer(draft: AnswerDraft, saved: SavedAnswer) {
  draft.remote = saved
  if (draft.saving) return
  if (draft.pending && sameAnswer(draft.pending.selected, saved.selected)) {
    acknowledgeAnswer(draft, draft.pending, saved); return
  }
  if (!draft.dirty && !draft.pending) {
    draft.selected = [...saved.selected]; draft.base = saved; draft.conflict = false; return
  }
  if (saved.revision !== draft.base.revision && !sameAnswer(draft.selected, saved.selected)) draft.conflict = true
  else if (sameAnswer(draft.selected, saved.selected)) {
    draft.base = saved; draft.dirty = false; draft.conflict = false; draft.pending = null
  }
}
export function prepareAnswer(draft: AnswerDraft, requestId: string): AnswerRequest | null {
  if ((!draft.dirty && !draft.pending) || draft.conflict || draft.saving) return null
  draft.pending ??= { requestId, revision: draft.base.revision, selected: [...draft.selected] }
  draft.saving = true
  return draft.pending
}
export function acceptRemoteAnswer(draft: AnswerDraft) {
  draft.selected = [...draft.remote.selected]; draft.base = draft.remote
  draft.dirty = false; draft.conflict = false; draft.pending = null; draft.saving = false
}
export function overwriteRemoteAnswer(draft: AnswerDraft) {
  draft.base = draft.remote; draft.conflict = false; draft.pending = null; draft.saving = false
  draft.dirty = !sameAnswer(draft.selected, draft.remote.selected)
}
