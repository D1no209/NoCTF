<script setup lang="ts">
import { ref } from 'vue'

const open = ref(false)

function close() {
  open.value = false
}
</script>

<template>
  <div class="relative inline-block text-left" @keydown.esc="close">
    <button type="button" @click="open = !open">
      <slot name="trigger" />
    </button>
    <div
      v-if="open"
      class="absolute right-0 z-50 mt-2 min-w-40 rounded-md border bg-popover p-1 text-popover-foreground shadow-md"
      @click="close"
    >
      <slot />
    </div>
  </div>
</template>
