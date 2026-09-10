import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'

export function typewriterFrames(texts: string[], progress: number) {
  let start = 0
  const total = texts.reduce((sum, text) => sum + Array.from(text).length, 0)
  return texts.map(text => {
    const characters = Array.from(text)
    const end = start + characters.length
    const frame = { text, visible: characters.slice(0, Math.max(0, progress - start)).join(''), active: progress >= start && progress < end && progress < total }
    start = end
    return frame
  })
}

/** One bounded sequence per output change. No timers remain after completion. */
export function useTypewriterMotion(texts: () => string[], ready: () => boolean) {
  const progress = ref(0)
  const total = computed(() => texts().reduce((sum, text) => sum + Array.from(text).length, 0))
  const frames = computed(() => typewriterFrames(texts(), ready() ? progress.value : 0).map(frame => ({ ...frame, active: ready() && frame.active })))
  let timer: ReturnType<typeof setTimeout> | undefined
  let media: MediaQueryList | undefined
  let mounted = false
  function stop() { if (timer !== undefined) clearTimeout(timer); timer = undefined }
  function schedule() {
    stop()
    if (!mounted || !ready()) return
    if (media?.matches) { progress.value = total.value; return }
    if (document.hidden || progress.value >= total.value) return
    timer = setTimeout(() => { progress.value++; schedule() }, 42)
  }
  function restart() { progress.value = 0; schedule() }
  watch([() => texts().join('\u0000'), ready], restart)
  onMounted(() => {
    mounted = true
    media = window.matchMedia('(prefers-reduced-motion: reduce)')
    media.addEventListener('change', schedule)
    document.addEventListener('visibilitychange', schedule)
    schedule()
  })
  onBeforeUnmount(() => {
    mounted = false
    stop()
    media?.removeEventListener('change', schedule)
    document.removeEventListener('visibilitychange', schedule)
  })
  return { frames }
}
