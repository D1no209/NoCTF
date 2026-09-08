<script setup lang="ts">
import Feature from '~/features/routes/admin/competitions/[id]/challenges/AdminCompetitionsByIdChallengesIndexPage.vue'
definePageMeta({ middleware: 'auth' })
</script>

<template>
  <Feature><slot /></Feature>
</template>
