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
  if (progress.value > 50) return 'bg-green-500'
  if (progress.value > 20) return 'bg-yellow-500'
  return 'bg-red-500'
})

function formatTime(seconds: number) {
  const m = Math.floor(seconds / 60)
  const s = seconds % 60
  return `${m}:${s.toString().padStart(2, '0')}`
}
</script>

<template>
  <div class="flex items-center gap-6">
    <div :key="fadeKey" class="round-fade">
      <span class="text-sm text-muted-foreground uppercase tracking-widest">{{ t('common.round') }}</span>
      <span class="text-4xl font-bold tabular-nums ml-2">{{ round }}</span>
    </div>

    <div class="flex-1 max-w-xs">
      <div class="flex justify-between text-sm text-muted-foreground mb-1">
        <span>{{ t('common.timeRemaining') }}</span>
        <span class="font-mono tabular-nums">{{ formatTime(remainingSeconds) }}</span>
      </div>
      <div class="h-2 bg-muted rounded-full overflow-hidden">
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
