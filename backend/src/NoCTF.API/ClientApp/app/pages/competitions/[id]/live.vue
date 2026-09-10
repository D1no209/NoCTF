<script setup lang="ts">
import Feature from '~/features/routes/competitions/[id]/CompetitionsByIdLivePage.vue'
definePageMeta({ layout: false, alias: ['/competitions/:id/live-'] })
</script>

<template>
  <Feature><slot /></Feature>
</template>
