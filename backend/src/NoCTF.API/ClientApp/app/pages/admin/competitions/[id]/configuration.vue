<script setup lang="ts">
import Feature from '~/features/routes/admin/competitions/[id]/AdminCompetitionsByIdConfigurationPage.vue'
definePageMeta({ middleware: 'competition-admin' })
</script>

<template>
  <Feature><slot /></Feature>
</template>
