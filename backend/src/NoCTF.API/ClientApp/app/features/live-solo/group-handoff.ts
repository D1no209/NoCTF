import type { GroupDraft } from './group-draft'

const drafts = new Map<string, { draft: GroupDraft; original: string }>()

/** A short-lived, identity-scoped handoff; no future question content is persisted. */
export function keepGroupDraft(key: string, draft: GroupDraft, original: string) {
  drafts.set(key, { draft: { ...draft, questions: draft.questions.map(x => ({ ...x })) }, original })
}
export function takeGroupDraft(key: string) {
  const value = drafts.get(key)
  drafts.delete(key)
  return value
}
