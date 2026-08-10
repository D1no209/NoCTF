<script setup lang="ts">
import { Moon, Sun } from '@lucide/vue'

const { isDark, toggle } = useTheme()
const { t } = useLocale()
</script>

<template>
  <Button
    variant="ghost"
    size="icon"
    :aria-label="isDark ? t('切换为浅色模式') : t('切换为深色模式')"
    @click="toggle()"
  >
    <Sun v-if="isDark" />
    <Moon v-else />
  </Button>
</template>
