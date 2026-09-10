<script setup lang="ts">
import Feature from '~/features/routes/admin/AdminPlatformPage.vue'
definePageMeta({ middleware: 'platform-admin' })
</script>

<template>
  <div data-slot="platform-admin-page" class="min-w-0">
    <Feature><slot /></Feature>
  </div>
</template>
