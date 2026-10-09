<script setup lang="ts">
import { onScopeDispose, ref, watch } from 'vue'
import type Hls from 'hls.js'
import { Empty, EmptyHeader, EmptyTitle } from '../empty'
import { Button } from '../button'
import { fragmentAtPlaybackTime } from './media-timeline'
const props = defineProps<{ source: string | null; label: string; waitingLabel: string; errorLabel: string; retryLabel: string;
  playLabel: string; requestHeaders?: () => Record<string, string> }>()
const emit = defineEmits<{ fragment: [url: string]; failed: []; retry: [] }>()
const video = ref<HTMLVideoElement | null>(null), failed = ref(false)
const playing = ref(false)
let player: Hls | null = null, sequence = 0, disposed = false
let currentFragment: string | null = null
const fragments = new Map<string, { url: string; start: number; end: number }>()
function clear() { player?.destroy(); player = null; currentFragment = null; fragments.clear(); playing.value = false; if (video.value) { video.value.pause(); video.value.removeAttribute('src'); video.value.load() } }
function syncFragment() {
  if (!video.value || video.value.paused && !playing.value) return
  const at = video.value.currentTime
  const url = fragmentAtPlaybackTime(fragments.values(), at, playing.value)
  if (url && url !== currentFragment) { currentFragment = url; emit('fragment', url) }
}
function started() { playing.value = true; syncFragment() }
async function play() { try { await video.value?.play(); started() } catch { failed.value = true; emit('failed') } }
function paused() { playing.value = false }
async function load() {
  const id = ++sequence; clear(); failed.value = false
  if (!props.source || !video.value) return
  try {
    const url = new URL(props.source, window.location.origin)
    if (url.origin !== window.location.origin || !['http:', 'https:'].includes(url.protocol)) throw new Error('Invalid video origin')
    const sdk = await import('hls.js')
    if (disposed || sequence !== id) return
    if (!sdk.default.isSupported()) throw new Error('Unsupported video transport')
    player = new sdk.default({ lowLatencyMode: false, liveSyncDurationCount: 2, backBufferLength: 30, maxBufferLength: 20,
      xhrSetup: (xhr, requestUrl) => {
        if (new URL(requestUrl, url).origin !== window.location.origin) throw new Error('Invalid video request origin')
        for (const [name, value] of Object.entries(props.requestHeaders?.() ?? {})) xhr.setRequestHeader(name, value)
      } })
    player.on(sdk.Events.FRAG_CHANGED, (_event, data) => { if (sequence === id) { fragments.set(data.frag.url, { url: data.frag.url, start: data.frag.start, end: data.frag.start + data.frag.duration }); syncFragment() } })
    player.on(sdk.Events.ERROR, (_event, data) => { if (sequence === id && data.fatal) { failed.value = true; emit('failed'); clear() } })
    player.loadSource(url.href); player.attachMedia(video.value!)
  }
  catch { if (sequence === id) { failed.value = true; emit('failed') } }
}
function retry() { emit('retry'); void load() }
watch([video, () => props.source], () => { void load() }, { flush: 'post' })
onScopeDispose(() => { disposed = true; sequence++; clear() })
</script>
<template>
  <div class="relative aspect-video min-w-0 rounded-xl bg-muted">
    <video v-show="source && !failed" ref="video" class="h-full w-full rounded-xl object-contain" controls playsinline :aria-label="label" @pause="paused" @play="started" @timeupdate="syncFragment" @seeked="syncFragment" />
    <Button v-if="source && !failed && !playing" class="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2" @click="play">{{ playLabel }}</Button>
    <Empty v-if="!source || failed" class="h-full"><EmptyHeader><EmptyTitle class="text-sm">{{ failed ? errorLabel : waitingLabel }}</EmptyTitle></EmptyHeader><Button v-if="failed" size="sm" variant="outline" @click="retry">{{ retryLabel }}</Button></Empty>
  </div>
</template>
