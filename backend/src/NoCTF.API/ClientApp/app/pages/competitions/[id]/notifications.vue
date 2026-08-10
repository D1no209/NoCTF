<script setup lang="ts">
const route = useRoute()
const notification = typeof route.query.notification === 'string'
  ? route.query.notification
  : undefined

await navigateTo({
  path: '/notifications',
  query: notification ? { notification } : {},
}, { replace: true })
</script>

<template>
  <div />
</template>
