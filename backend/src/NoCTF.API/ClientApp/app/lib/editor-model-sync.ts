export interface TextChange {
  from: number
  to: number
  insert: string
}

/**
 * Smallest single replacement that turns `current` into `next` by trimming the
 * common prefix and suffix. Applying this instead of replacing the whole
 * document keeps the CodeMirror selection mapped to the same text, so an
 * external model write (form reset, loaded draft, format switch) no longer
 * jumps the caret to the end or drops characters typed in the same frame.
 */
export function minimalTextChange(current: string, next: string): TextChange | null {
  if (current === next) return null
  const limit = Math.min(current.length, next.length)
  let prefix = 0
  while (prefix < limit && current[prefix] === next[prefix]) prefix += 1
  let suffix = 0
  while (suffix < limit - prefix
    && current[current.length - 1 - suffix] === next[next.length - 1 - suffix]) suffix += 1
  return {
    from: prefix,
    to: current.length - suffix,
    insert: next.slice(prefix, next.length - suffix),
  }
}

/**
 * An incoming model value must reach the editor only when it really is an
 * external change: an equal document needs no write, and values we just emitted
 * are echoes of the editor's own state (a stale echo would otherwise rewind the
 * document and lose the characters typed in between).
 */
export function shouldApplyExternalModel(
  currentDoc: string,
  incoming: string | null | undefined,
  lastEmitted: string,
): boolean {
  if (typeof incoming !== 'string') return false
  if (incoming === currentDoc) return false
  if (incoming === lastEmitted) return false
  return true
}
