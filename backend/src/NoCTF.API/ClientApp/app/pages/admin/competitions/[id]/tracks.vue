<script setup lang="ts">
import Feature from '~/features/routes/admin/competitions/[id]/AdminCompetitionsByIdTracksPage.vue'
definePageMeta({ middleware: 'competition-admin' })
</script>

<template>
  <Feature><slot /></Feature>
</template>
