<script setup lang="ts">
import Feature from '~/features/routes/account/AccountIndexPage.vue'
definePageMeta({ middleware: 'auth' })
</script>

<template>
  <Feature><slot /></Feature>
</template>
