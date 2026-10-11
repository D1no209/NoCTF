<script setup lang="ts">
definePageMeta({ middleware: 'competition-admin' })
import Feature from '~/features/routes/competitions/[id]/CompetitionsByIdEventsPage.vue'

</script>

<template>
  <Feature><slot /></Feature>
</template>
