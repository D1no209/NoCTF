import { getTheoryPaperImage, getTheoryQuestionImage } from '~/api'

export function useTheoryAssets() {
  const assets = shallowRef<Record<string, string>>({})
  let epoch = 0
  let scopeKey = ''
  async function load(sources: string[], scope: { questionId: string } | { activityId: string; paperId: string; staff?: boolean }) {
    const nextScope = JSON.stringify(scope)
    if (nextScope !== scopeKey) { clear(); scopeKey = nextScope }
    const current = ++epoch
    const ids = [...new Set(sources.flatMap(source => [...source.matchAll(/theory-file:([a-f\d-]{36})/gi)].map(match => match[1]!)))]
    const next: Record<string, string> = {}
    await Promise.all(ids.map(async fileId => {
      const key = `theory-file:${fileId}`
      if (assets.value[key]) { next[key] = assets.value[key]!; return }
      const response = 'questionId' in scope
        ? await getTheoryQuestionImage({ path: { questionId: scope.questionId, fileId }, parseAs: 'blob' })
        : await getTheoryPaperImage({ path: { activityId: scope.activityId, paperId: scope.paperId, fileId }, query: { staff: scope.staff ?? false }, parseAs: 'blob' })
      const blob: unknown = response.data
      if (response.error || !(blob instanceof Blob)) return
      const url = URL.createObjectURL(blob)
      if (current !== epoch) URL.revokeObjectURL(url)
      else next[key] = url
    }))
    if (current !== epoch) { const retained = new Set(Object.values(assets.value)); Object.values(next).forEach(url => { if (!retained.has(url)) URL.revokeObjectURL(url) }); return }
    for (const [key, url] of Object.entries(assets.value)) if (!next[key]) URL.revokeObjectURL(url)
    assets.value = next
  }
  function clear() { epoch++; Object.values(assets.value).forEach(url => URL.revokeObjectURL(url)); assets.value = {} }
  onBeforeUnmount(clear)
  return { assets, load, clear }
}
