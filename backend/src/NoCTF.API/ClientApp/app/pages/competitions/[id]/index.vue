<script setup lang="ts">
import Feature from '~/features/routes/competitions/CompetitionsIndexPage.vue'
definePageMeta({ key: 'competition-browser-content' })
</script>

<template>
  <Feature><slot /></Feature>
</template>
