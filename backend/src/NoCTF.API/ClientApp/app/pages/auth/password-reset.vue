<script setup lang="ts">
import Feature from '~/features/routes/auth/AuthPasswordResetPage.vue'
definePageMeta({ middleware: 'guest' })
</script>

<template>
  <Feature><slot /></Feature>
</template>
