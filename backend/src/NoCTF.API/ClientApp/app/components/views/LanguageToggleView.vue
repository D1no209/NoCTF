<script setup lang="ts">
import { toRefs } from 'vue'
import type { LanguageToggleViewState } from '~/features/useLanguageToggle'

const viewProps = defineProps<{ state: LanguageToggleViewState }>()
const { Languages, isEnglish, switchLocale, t } = toRefs(viewProps.state)
</script>

<template>
  <Button
    variant="ghost"
    size="sm"
    class="gap-1.5 px-2 font-medium"
    :aria-label="isEnglish ? t('ui.switchToChinese') : t('ui.switchToEnglish')"
    @click="switchLocale"
  >
    <Languages class="size-4" />
    <span aria-hidden="true">{{ isEnglish ? $t('locale.switchToChineseShort') : $t('locale.switchToEnglishShort') }}</span>
  </Button>
</template>
