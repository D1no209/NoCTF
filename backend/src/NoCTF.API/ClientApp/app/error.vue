<script setup lang="ts">
import type { NuxtError } from '#app'
import Feature from '~/features/shell/ApplicationError.vue'

defineProps<{ error: NuxtError }>()
</script>

<template>
  <Feature :error="error" />
</template>
