<script setup lang="ts">
import Feature from '~/features/routes/admin/competitions/[id]/AdminCompetitionsByIdLeaderboardPage.vue'
definePageMeta({ middleware: 'platform-admin' })
</script>

<template>
  <Feature><slot /></Feature>
</template>
