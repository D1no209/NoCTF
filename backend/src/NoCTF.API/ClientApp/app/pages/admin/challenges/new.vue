<script setup lang="ts">
import Feature from '~/features/routes/admin/challenges/AdminChallengesIndexPage.vue'
definePageMeta({ middleware: 'auth' })
</script>

<template>
  <Feature><slot /></Feature>
</template>
