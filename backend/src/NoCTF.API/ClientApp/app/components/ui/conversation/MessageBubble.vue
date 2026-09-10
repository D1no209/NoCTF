<script setup lang="ts">
defineProps<{ own: boolean }>()
</script>

<template>
  <li data-slot="message-bubble" :data-own="own" class="flex min-w-0 max-w-[85%] flex-col gap-1.5" :class="own ? 'self-end items-end' : 'self-start items-start'">
    <div class="flex flex-wrap items-center gap-2 text-xs text-muted-foreground" :class="own && 'justify-end'">
      <slot name="meta" />
    </div>
    <div data-slot="message-body" class="max-w-full px-4 py-3 text-sm leading-relaxed whitespace-pre-wrap break-words [overflow-wrap:anywhere]"><slot /></div>
  </li>
</template>
