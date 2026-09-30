<script setup lang="ts">
import Feature from '~/features/routes/competitions/CompetitionsByIdPage.vue'
import { competitionPageKey } from '~/utils/app-routes'

definePageMeta({
  path: '/competitions/:id?',
  key: competitionPageKey,
})
</script>

<template>
  <div data-slot="competition-page-route">
    <Feature><slot /></Feature>
  </div>
</template>
