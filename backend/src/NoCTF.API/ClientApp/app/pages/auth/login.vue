<script setup lang="ts">
import Feature from '~/features/routes/auth/AuthLoginPage.vue'
definePageMeta({ middleware: 'guest' })
</script>

<template>
  <Feature><slot /></Feature>
</template>
