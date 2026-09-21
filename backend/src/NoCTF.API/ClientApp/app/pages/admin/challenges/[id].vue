<script setup lang="ts">
import Feature from '~/features/routes/admin/challenges/AdminChallengesByIdPage.vue'
definePageMeta({ middleware: 'organizer' })
</script>

<template>
  <Feature><slot /></Feature>
</template>
