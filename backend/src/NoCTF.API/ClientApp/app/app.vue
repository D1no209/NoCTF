<script setup lang="ts">
usePlatform().ensureLoaded()
const { isDark } = useTheme()
const { locale } = useLocale()
</script>

<template>
  <NuxtLayout>
    <NuxtPage :key="locale" />
  </NuxtLayout>
  <Toaster rich-colors close-button position="top-right" :theme="isDark ? 'dark' : 'light'" />
</template>
