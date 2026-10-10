<script setup lang="ts">
import Feature from '~/features/routes/admin/competitions/[id]/AdminCompetitionsByIdWriteUpsPage.vue'

definePageMeta({ middleware: 'competition-admin' })
</script>

<template>
  <div class="flex h-full min-h-0 flex-col">
    <Feature />
  </div>
</template>
