<script setup lang="ts">
import Feature from '~/features/routes/competitions/[id]/challenges/CompetitionsByIdChallengesIndexPage.vue'

definePageMeta({
  key: route => route.params.id as string,
})
</script>

<template>
  <Feature><slot /></Feature>
</template>
