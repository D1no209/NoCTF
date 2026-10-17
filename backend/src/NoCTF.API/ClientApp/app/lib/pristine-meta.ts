/**
 * Whether a refreshed resource may overwrite one form field.
 *
 * The field is written only while it still equals the value from the previous
 * hydration, so unsaved edits survive realtime refreshes. First hydration,
 * competition switches and successful saves pass `force`.
 */
export function shouldHydrateMetaField<T extends Record<string, unknown>>(
  previous: T | null,
  current: T,
  key: keyof T,
  force = false,
): boolean {
  if (force || previous === null) return true
  return current[key] === previous[key]
}
