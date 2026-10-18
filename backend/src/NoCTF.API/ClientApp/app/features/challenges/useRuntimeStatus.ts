import type { ComputedRef, Ref } from 'vue'
import { createRuntimeExtensionRequest, isRuntimeExtensionTooEarly, parseRuntimeExtensionMinutes } from '../../lib/runtime-extension'

/** The runtime fields shared by the administration and participant runtime surfaces. */
export interface RuntimeStatusSource {
  id?: string | null
  state?: string | null
  expiresAt?: string | null
}

/** One-second clock for TTL and renewal windows; cleared with the owning scope. */
export function useRuntimeClock(): Ref<number> {
  const now = ref(Date.now())
  let timer: ReturnType<typeof setInterval> | undefined
  onMounted(() => {
    timer = setInterval(() => { now.value = Date.now() }, 1000)
  })
  onUnmounted(() => {
    if (timer) clearInterval(timer)
  })
  return now
}

/** Remaining runtime lifetime label, or null when nothing is provisioned. */
export function useRuntimeTtl(runtime: Ref<RuntimeStatusSource | null>, now: Ref<number>) {
  return computed(() => {
    const expiresAt = runtime.value?.expiresAt
    if (!expiresAt) return null
    const remaining = new Date(expiresAt).getTime() - now.value
    return remaining > 0 ? formatDuration(remaining) : translate('runtime.label.expired')
  })
}

/** Shared badge tone for every runtime state surface. */
export function useRuntimeStateVariant(runtime: Ref<RuntimeStatusSource | null>) {
  return computed(() => {
    switch (runtime.value?.state) {
      case 'Running': return 'default' as const
      case 'Failed': return 'destructive' as const
      case 'Stopped': return 'secondary' as const
      default: return 'outline' as const
    }
  })
}

export interface RuntimeExtensionOptions {
  /** Upper bound accepted by the runtime extension request. */
  maximumMinutes: number
  /** Whether an in-flight command or poll should block renewal. */
  busy: ComputedRef<boolean>
}

/**
 * Shared renewal window for both runtime surfaces: the minute input, its
 * validation, the too-early state and the request body. Whether a runtime is
 * renewed at all stays with the owning controller.
 */
export function useRuntimeExtension(
  runtime: Ref<RuntimeStatusSource | null>,
  now: Ref<number>,
  options: RuntimeExtensionOptions,
) {
  const extendMinutes = ref<number | string>(30)
  const extendMinutesInvalid = computed(() =>
    parseRuntimeExtensionMinutes(extendMinutes.value, options.maximumMinutes) === null)
  const renewalTooEarly = computed(() => runtime.value?.state === 'Running'
    && isRuntimeExtensionTooEarly(runtime.value?.expiresAt, now.value))
  const extensionRequest = computed(() => createRuntimeExtensionRequest(
    runtime.value?.expiresAt, now.value, extendMinutes.value, options.maximumMinutes))
  const extensionWindowOpen = computed(() => runtime.value?.state === 'Running'
    && extensionRequest.value !== null)
  const validExtension = computed(() => extensionWindowOpen.value && !options.busy.value)
  return { extendMinutes, extendMinutesInvalid, renewalTooEarly, extensionRequest, extensionWindowOpen, validExtension }
}
