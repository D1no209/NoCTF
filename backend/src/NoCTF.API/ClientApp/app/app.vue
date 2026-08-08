<script setup lang="ts">
usePlatform().ensureLoaded()
const { isDark } = useTheme()
</script>

<template>
  <NuxtLayout>
    <NuxtPage />
  </NuxtLayout>
  <Toaster rich-colors close-button position="top-right" :theme="isDark ? 'dark' : 'light'" />
</template>
