<script setup lang="ts">
const { configuration, ensureLoaded } = usePlatform()
const { isDark } = useTheme()

useHead(() => ({
  link: configuration.value?.logoUrl
    ? [{ key: 'platform-icon', rel: 'icon', href: configuration.value.logoUrl }]
    : [],
}))

void ensureLoaded()
</script>

<template>
  <NuxtLayout>
    <NuxtPage />
  </NuxtLayout>
  <Toaster rich-colors close-button position="top-right" :theme="isDark ? 'dark' : 'light'" />
</template>
