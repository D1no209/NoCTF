<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useThemePackages } from '@/composables/useThemePackages'
import { getUiPackageComponent, resolveUiPackage } from '@/ui-package-registry'

// Application shell: pick the theme package for the current route and render
// its application root. Packages are independent — each owns its chrome,
// shells, and pages; uncovered routes fall back to the default package.
const { activeTheme } = useThemePackages()
const route = useRoute()

const activePackage = computed(() => resolveUiPackage(activeTheme.value.uiPackage, String(route.name)))
const activeApplication = computed(() => getUiPackageComponent(activePackage.value))
</script>

<template>
  <component :is="activeApplication" :key="activePackage.id" />
</template>
