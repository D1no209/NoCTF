<script setup lang="ts">
import { Languages } from '@lucide/vue'

const { isEnglish, switchLocale, t } = useLocale()
</script>

<template>
  <Button
    variant="ghost"
    size="sm"
    class="gap-1.5 px-2 font-medium"
    :aria-label="isEnglish ? t('切换为中文') : t('切换为英文')"
    @click="switchLocale"
  >
    <Languages class="size-4" />
    <span aria-hidden="true">{{ isEnglish ? '中' : 'EN' }}</span>
  </Button>
</template>
