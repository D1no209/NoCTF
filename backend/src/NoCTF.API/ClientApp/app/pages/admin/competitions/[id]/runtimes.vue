<script setup lang="ts">
import Feature from '~/features/routes/admin/competitions/[id]/AdminCompetitionsByIdRuntimesPage.vue'
definePageMeta({ middleware: 'competition-admin', path: '/admin/competitions/:id/runtimes/:runtimeId?', key: route => `/admin/competitions/${route.params.id}/runtimes` })
</script>

<template>
  <Feature><slot /></Feature>
</template>
