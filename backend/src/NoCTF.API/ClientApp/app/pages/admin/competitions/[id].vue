<script setup lang="ts">
import Feature from '~/features/routes/admin/competitions/AdminCompetitionsByIdPage.vue'
definePageMeta({ middleware: 'competition-admin' })
</script>

<template>
  <Feature><slot /></Feature>
</template>
