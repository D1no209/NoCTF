<script setup lang="ts">
import Feature from '~/features/routes/admin/competitions/[id]/AdminCompetitionsByIdTeamsPage.vue'
definePageMeta({ middleware: 'platform-admin', path: '/admin/competitions/:id/teams/:teamId?', key: route => `/admin/competitions/${route.params.id}/teams` })
</script>

<template>
  <Feature><slot /></Feature>
</template>
