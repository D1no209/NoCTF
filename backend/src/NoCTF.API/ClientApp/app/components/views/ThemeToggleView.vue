<script setup lang="ts">
import { toRefs } from 'vue'
import type { ThemeToggleViewState } from '~/features/useThemeToggle'

const viewProps = defineProps<{ state: ThemeToggleViewState }>()
const { Moon, Sun, isDark, themeTransitioning, toggle, t } = toRefs(viewProps.state)
</script>

<template>
  <Button
    variant="ghost"
    size="icon"
    class="rounded-full"
    :aria-label="isDark ? t('ui.switchToLightTheme') : t('ui.switchToDarkTheme')"
    :aria-busy="themeTransitioning || undefined"
    :disabled="themeTransitioning"
    @click="toggle()"
  >
    <Sun v-if="isDark" />
    <Moon v-else />
  </Button>
</template>
