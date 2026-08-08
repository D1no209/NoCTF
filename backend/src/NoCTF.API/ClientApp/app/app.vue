<script setup lang="ts">
usePlatform().ensureLoaded()
</script>

<template>
  <NuxtLayout>
    <NuxtPage />
  </NuxtLayout>
  <Toaster rich-colors close-button position="top-right" />
</template>
