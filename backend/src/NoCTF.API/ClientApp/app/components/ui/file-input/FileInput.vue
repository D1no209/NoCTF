<script setup lang="ts">
// Use a dedicated primitive: file inputs must never receive a text v-model.
</script>

<template>
  <input type="file" data-slot="file-input" class="sr-only" tabindex="-1" aria-hidden="true">
</template>
