<script setup lang="ts">
import { Moon, Sun } from '@lucide/vue'

const { isDark, toggle } = useTheme()
</script>

<template>
  <Button
    variant="ghost"
    size="icon"
    :aria-label="isDark ? '切换为浅色模式' : '切换为深色模式'"
    @click="toggle()"
  >
    <Sun v-if="isDark" />
    <Moon v-else />
  </Button>
</template>
