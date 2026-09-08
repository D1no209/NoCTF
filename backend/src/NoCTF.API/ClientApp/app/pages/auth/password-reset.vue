<script setup lang="ts">
import Feature from '~/features/routes/auth/AuthPasswordResetPage.vue'
definePageMeta({ middleware: 'guest', alias: ['/reset-password'] })
</script>

<template>
  <Feature><slot /></Feature>
</template>
