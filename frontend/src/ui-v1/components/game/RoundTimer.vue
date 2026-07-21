<script setup lang="ts">
import { computed, watch, ref } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  round: number
  remainingSeconds: number
  totalSeconds: number
}>()

const { t } = useI18n()

const fadeKey = ref(0)

watch(
  () => props.round,
  () => {
    fadeKey.value++
  },
)

const progress = computed(() =>
  props.totalSeconds > 0
    ? Math.max(0, Math.min(100, (props.remainingSeconds / props.totalSeconds) * 100))
    : 0,
)

const progressColor = computed(() => {
  if (progress.value > 50) return 'bg-[var(--semantic-success)]'
  if (progress.value > 20) return 'bg-[var(--semantic-warning)]'
  return 'bg-[var(--semantic-danger)]'
})

function formatTime(seconds: number) {
  const m = Math.floor(seconds / 60)
  const s = seconds % 60
  return `${m}:${s.toString().padStart(2, '0')}`
}
</script>

<template>
  <div class="flex flex-col gap-4 sm:flex-row sm:items-center sm:gap-6">
    <div :key="fadeKey" class="round-fade min-w-[9rem]">
      <span>{{ t('common.round') }}</span>
      <span class="ml-2 text-4xl font-bold tabular-nums">{{ round }}</span>
    </div>

    <div class="w-full max-w-xl flex-1">
      <div class="mb-1 flex justify-between text-sm text-muted-foreground">
        <span>{{ t('common.timeRemaining') }}</span>
        <span class="font-mono tabular-nums">{{ formatTime(remainingSeconds) }}</span>
      </div>
      <div class="h-2 overflow-hidden rounded-full bg-muted">
        <div
          class="h-full rounded-full transition-all duration-1000"
          :class="progressColor"
          :style="{ width: `${progress}%` }"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.round-fade {
  animation: fadeIn 0.4s ease-out;
}

@keyframes fadeIn {
  from {
    opacity: 0;
    transform: translateY(-6px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
</style>
