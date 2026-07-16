<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Globe2 } from 'lucide-vue-next'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'

const { locale, t } = useI18n()

const options = computed(() => [
  { value: 'en', label: t('common.locale.en') },
  { value: 'zh-CN', label: t('common.locale.zhCN') },
])

const selected = ref(locale.value)

watch(selected, (val) => {
  locale.value = val
  localStorage.setItem('locale', val)
})
</script>

<template>
  <Select v-model="selected">
    <SelectTrigger class="relative w-auto min-w-[8rem] pl-9 uppercase tracking-[0.12em]">
      <Globe2 class="absolute left-3 size-4 text-muted-foreground" />
      <SelectValue />
    </SelectTrigger>
    <SelectContent :body-lock="false" :disable-outside-pointer-events="false">
      <SelectItem
        v-for="opt in options"
        :key="opt.value"
        :value="opt.value"
      >
        {{ opt.label }}
      </SelectItem>
    </SelectContent>
  </Select>
</template>
