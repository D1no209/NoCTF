<script setup lang="ts">
import Feature from '~/features/routes/competitions/[id]/CompetitionsByIdEventsPage.vue'

definePageMeta({ middleware: 'competition-admin' })
</script>

<template><Feature embedded /></template>
