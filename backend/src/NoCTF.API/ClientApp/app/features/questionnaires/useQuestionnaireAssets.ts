import { getQuestionnaireSessionImage, adminChallengeBankGetQuestionnaireImage } from '~/api'

export function useQuestionnaireAssets() {
  const assets = shallowRef<Record<string, string>>({})
  let epoch = 0
  let scopeKey = ''
  let pending: { key: string; task: Promise<void> } | undefined
  function load(sources: string[], scope: { challengeId: string } | { activityId: string; sessionId: string; staff?: boolean }): Promise<void> {
    const nextScope = JSON.stringify(scope)
    if (nextScope !== scopeKey) { clear(); scopeKey = nextScope }
    const ids = [...new Set(sources.flatMap(source => [...source.matchAll(/questionnaire-file:([a-f\d-]{36})/gi)].map(match => match[1]!)))].sort()
    const key = JSON.stringify(ids)
    if (pending?.key === key) return pending.task
    const current = ++epoch
    pending = undefined
    if (ids.length === Object.keys(assets.value).length && ids.every(id => assets.value[`questionnaire-file:${id}`]))
      return Promise.resolve()
    const task = (async () => {
      const next: Record<string, string> = {}
      await Promise.all(ids.map(async fileId => {
        const key = `questionnaire-file:${fileId}`
        if (assets.value[key]) { next[key] = assets.value[key]!; return }
        try {
          const response = 'challengeId' in scope
            ? await adminChallengeBankGetQuestionnaireImage({ path: { challengeId: scope.challengeId, fileId }, parseAs: 'blob' })
            : await getQuestionnaireSessionImage({ path: { activityId: scope.activityId, sessionId: scope.sessionId, fileId }, query: { staff: scope.staff ?? false }, parseAs: 'blob' })
          const blob: unknown = response.data
          if (response.error || !(blob instanceof Blob)) return
          const url = URL.createObjectURL(blob)
          if (current !== epoch) URL.revokeObjectURL(url)
          else next[key] = url
        }
        catch { /* 保留已加载资源,下一次刷新只重试未加载的图片。 */ }
      }))
      if (current !== epoch) { const retained = new Set(Object.values(assets.value)); Object.values(next).forEach(url => { if (!retained.has(url)) URL.revokeObjectURL(url) }); return }
      for (const [key, url] of Object.entries(assets.value)) if (!next[key]) URL.revokeObjectURL(url)
      if (Object.keys(next).length !== Object.keys(assets.value).length || Object.entries(next).some(([key, url]) => assets.value[key] !== url))
        assets.value = next
    })().finally(() => { if (pending?.task === task) pending = undefined })
    pending = { key, task }
    return task
  }
  function clear() { epoch++; pending = undefined; scopeKey = ''; Object.values(assets.value).forEach(url => URL.revokeObjectURL(url)); assets.value = {} }
  onBeforeUnmount(clear)
  return { assets, load, clear }
}
