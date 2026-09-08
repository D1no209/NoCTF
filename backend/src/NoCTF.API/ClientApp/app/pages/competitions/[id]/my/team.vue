<script setup lang="ts">
import Feature from '~/features/routes/competitions/[id]/my/CompetitionsByIdMyTeamPage.vue'
definePageMeta({ middleware: 'auth' })
</script>

<template>
  <Feature><slot /></Feature>
</template>
