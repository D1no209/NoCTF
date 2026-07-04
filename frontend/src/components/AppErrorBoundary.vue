<script setup lang="ts">
import { onBeforeUnmount, onErrorCaptured, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import CrashView from '@/views/CrashView.vue'
import type { AppErrorDetails } from '@/types/app-error'

const route = useRoute()
const capturedError = ref<AppErrorDetails | null>(null)

function errorMessage(error: unknown) {
  if (error instanceof Error) return error.message
  if (typeof error === 'string') return error

  try {
    return JSON.stringify(error)
  } catch {
    return String(error)
  }
}

function errorStack(error: unknown) {
  return error instanceof Error ? error.stack : undefined
}

function buildErrorDetails(error: unknown, source: string, info?: string): AppErrorDetails {
  return {
    id: `crash-${Date.now().toString(36)}`,
    message: errorMessage(error),
    stack: errorStack(error),
    info,
    source,
    path: route.fullPath,
    time: new Date().toISOString(),
  }
}

function capture(error: unknown, source: string, info?: string) {
  if (capturedError.value) return

  const details = buildErrorDetails(error, source, info)
  capturedError.value = details

  window.dispatchEvent(new CustomEvent('noctf:app-crash', { detail: details }))
  console.error('[NoCTF crash]', details)
}

function handleWindowError(event: ErrorEvent) {
  const source = event.filename
    ? `window:${event.filename}:${event.lineno}:${event.colno}`
    : 'window'
  capture(event.error ?? event.message, source)
}

function handleUnhandledRejection(event: PromiseRejectionEvent) {
  capture(event.reason, 'unhandledrejection')
}

onErrorCaptured((error, _instance, info) => {
  capture(error, 'vue', info)
  return false
})

onMounted(() => {
  window.addEventListener('error', handleWindowError)
  window.addEventListener('unhandledrejection', handleUnhandledRejection)
})

onBeforeUnmount(() => {
  window.removeEventListener('error', handleWindowError)
  window.removeEventListener('unhandledrejection', handleUnhandledRejection)
})

watch(
  () => route.fullPath,
  () => {
    capturedError.value = null
  },
)
</script>

<template>
  <CrashView v-if="capturedError" :error="capturedError" />
  <slot v-else />
</template>
