<script setup lang="ts">
import Feature from '~/features/routes/competitions/[id]/my/CompetitionsByIdMySubmissionsPage.vue'
definePageMeta({ middleware: 'auth' })
</script>

<template>
  <Feature><slot /></Feature>
</template>
