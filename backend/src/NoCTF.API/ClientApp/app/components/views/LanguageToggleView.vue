<script setup lang="ts">
import { toRefs } from 'vue'
import type { LanguageToggleViewState } from '~/features/useLanguageToggle'

const viewProps = defineProps<{ state: LanguageToggleViewState }>()
const { Languages, isEnglish, switchLocale, t } = toRefs(viewProps.state)
</script>

<template>
  <Button
    variant="ghost"
    size="icon"
    class="rounded-full"
    :aria-label="isEnglish ? t('common.label.switchChinese') : t('common.label.switchEnglish')"
    @click="switchLocale"
  >
    <Languages class="size-4" />
  </Button>
</template>
