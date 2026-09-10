<script setup lang="ts">
definePageMeta({
  redirect: route => ({ path: '/competitions', query: { ...route.query, competition: route.params.id } }),
})
</script>

<template>
  <NuxtPage />
</template>
