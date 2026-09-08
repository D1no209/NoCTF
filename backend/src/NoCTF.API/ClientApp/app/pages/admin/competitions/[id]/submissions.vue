<script setup lang="ts">
import Feature from '~/features/routes/admin/competitions/[id]/AdminCompetitionsByIdSubmissionsPage.vue'
definePageMeta({ middleware: 'auth' })
</script>

<template>
  <Feature><slot /></Feature>
</template>
