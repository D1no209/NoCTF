<script setup lang="ts">
defineProps<{ count: number }>()
</script>

<template>
  <span data-slot="remaining-attempts">
    <span aria-hidden="true">
      {{ $t('attempts.remainingPrefix') }}
      <span data-slot="remaining-attempt-count" :data-critical="count < 5 || undefined">{{ count }}</span>
      {{ $t('attempts.remainingSuffix') }}
    </span>
    <span class="sr-only">{{ $t('common.label.submissionsRemaining', { count }) }}</span>
  </span>
</template>
