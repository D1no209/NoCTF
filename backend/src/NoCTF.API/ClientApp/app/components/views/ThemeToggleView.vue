<script setup lang="ts">
import { toRefs } from 'vue'
import type { ThemeToggleViewState } from '~/features/useThemeToggle'

const viewProps = defineProps<{ state: ThemeToggleViewState }>()
const { Moon, Sun, isDark, toggle, t } = toRefs(viewProps.state)
</script>

<template>
  <Button
    variant="ghost"
    size="icon"
    :aria-label="isDark ? t('ui.switchToLightTheme') : t('ui.switchToDarkTheme')"
    @click="toggle()"
  >
    <Sun v-if="isDark" />
    <Moon v-else />
  </Button>
</template>
