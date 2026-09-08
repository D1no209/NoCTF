<script setup lang="ts">
import Feature from '~/features/routes/admin/platform/AdminPlatformLogsPage.vue'
definePageMeta({ middleware: 'platform-admin' })
</script>

<template>
  <Feature><slot /></Feature>
</template>
