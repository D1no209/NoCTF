<script setup lang="ts">
/** Unboxed action for table cells, navigation rows and control screens. */
withDefaults(defineProps<{ type?: 'button' | 'submit' | 'reset' }>(), { type: 'button' })
</script>

<template>
  <button :type="type" data-slot="action-button" class="cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-50"><slot /></button>
</template>
