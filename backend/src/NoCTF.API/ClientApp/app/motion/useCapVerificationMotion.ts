import { onBeforeUnmount, onMounted, ref, watch, type CSSProperties, type Ref } from 'vue'

export type CapVerificationVisualState = 'running' | 'success' | 'error'

interface CapBit {
  id: number
  value: '0' | '1'
  strong: boolean
  style: CSSProperties
}

const HEX = '0123456789abcdef'
const HASH_LENGTH = 32

function randomHexCharacter(): string {
  return HEX[Math.floor(Math.random() * HEX.length)]!
}

function randomHash(): string[] {
  return Array.from({ length: HASH_LENGTH }, randomHexCharacter)
}

function completedHash(): string[] {
  return `00000${Array.from({ length: HASH_LENGTH - 5 }, randomHexCharacter).join('')}`.split('')
}

function createBits(): CapBit[] {
  return Array.from({ length: 22 }, (_, id) => ({
    id,
    value: Math.random() > 0.5 ? '1' : '0',
    strong: Math.random() > 0.7,
    style: {
      '--cap-bit-top': `${3 + Math.random() * 58}px`,
      '--cap-bit-duration': `${0.75 + Math.random() * 0.75}s`,
      '--cap-bit-delay': `${-Math.random() * 1.5}s`,
      '--cap-bit-curve-a': `${-9 + Math.random() * 18}px`,
      '--cap-bit-curve-b': `${-7 + Math.random() * 14}px`,
      '--cap-bit-drift': `${-10 + Math.random() * 20}px`,
    } as CSSProperties,
  }))
}

/** Drives the bounded text frames for the CAP status primitive and disposes every timer. */
export function useCapVerificationMotion(state: Ref<CapVerificationVisualState>) {
  const characters = ref(randomHash())
  const flashEpochs = ref(Array.from({ length: HASH_LENGTH }, () => 0))
  const hotIndices = ref<number[]>([])
  const settled = ref(false)
  const bits = createBits()
  let mutationTimer: ReturnType<typeof setInterval> | undefined
  let completionTimer: ReturnType<typeof setInterval> | undefined
  let media: MediaQueryList | undefined
  let mounted = false

  function stopTimers(): void {
    if (mutationTimer !== undefined) clearInterval(mutationTimer)
    if (completionTimer !== undefined) clearInterval(completionTimer)
    mutationTimer = undefined
    completionTimer = undefined
  }

  function mutate(): void {
    const nextCharacters = [...characters.value]
    const nextEpochs = [...flashEpochs.value]
    const count = 4 + Math.floor(Math.random() * 5)
    const hot: number[] = []
    for (let index = 0; index < count; index++) {
      const target = Math.floor(Math.random() * HASH_LENGTH)
      nextCharacters[target] = randomHexCharacter()
      nextEpochs[target] = (nextEpochs[target] ?? 0) + 1
      if (Math.random() < 0.22) hot.push(target)
    }
    characters.value = nextCharacters
    flashEpochs.value = nextEpochs
    hotIndices.value = hot
  }

  function run(): void {
    stopTimers()
    settled.value = false
    hotIndices.value = []
    if (media?.matches) return
    mutationTimer = setInterval(mutate, 48)
  }

  function complete(): void {
    stopTimers()
    hotIndices.value = []
    const finalCharacters = completedHash()
    if (media?.matches) {
      characters.value = finalCharacters
      settled.value = true
      return
    }
    let cursor = 0
    completionTimer = setInterval(() => {
      const nextCharacters = [...characters.value]
      nextCharacters[cursor] = finalCharacters[cursor]!
      characters.value = nextCharacters
      cursor++
      if (cursor < HASH_LENGTH) return
      stopTimers()
      settled.value = true
    }, 11)
  }

  function applyState(): void {
    if (!mounted) return
    if (document.hidden) {
      stopTimers()
      return
    }
    if (state.value === 'running') run()
    else if (state.value === 'success') complete()
    else {
      stopTimers()
      settled.value = false
      hotIndices.value = []
    }
  }

  watch(state, applyState)
  onMounted(() => {
    mounted = true
    media = window.matchMedia('(prefers-reduced-motion: reduce)')
    media.addEventListener('change', applyState)
    document.addEventListener('visibilitychange', applyState)
    applyState()
  })
  onBeforeUnmount(() => {
    mounted = false
    stopTimers()
    media?.removeEventListener('change', applyState)
    document.removeEventListener('visibilitychange', applyState)
  })

  return { bits, characters, flashEpochs, hotIndices, settled }
}
