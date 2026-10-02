<script setup lang="ts">
import Feature from '~/features/routes/admin/platform/AdminPlatformRuntimesPage.vue'
definePageMeta({ middleware: 'platform-admin', path: '/admin/platform/runtimes/:runtimeId?', key: 'admin-platform-runtimes' })
</script>

<template>
  <Feature><slot /></Feature>
</template>
