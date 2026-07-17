<script setup lang="ts">
withDefaults(defineProps<{ variant?: 'frame' | 'center' }>(), { variant: 'frame' })
</script>

<template>
  <main class="relative min-h-dvh overflow-y-auto bg-transparent text-[var(--awdp-text)]">
    <div class="mx-auto flex w-full max-w-[1600px] flex-col gap-4 p-4 md:p-6">
      <slot />
    </div>
  </main>
</template>
