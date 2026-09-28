<script setup lang="ts">
import Feature from '~/features/routes/competitions/CompetitionsByIdPage.vue'

definePageMeta({
  key: route => route.params.id as string,
})
</script>

<template>
  <div data-slot="competition-page-route">
    <Feature><slot /></Feature>
  </div>
</template>
