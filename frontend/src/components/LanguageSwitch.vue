<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Select } from '@/components/ui/select'

const { locale } = useI18n()

const options = [
  { value: 'en', label: 'EN' },
  { value: 'zh-CN', label: '中文' },
]

const selected = ref(locale.value)

watch(selected, (val) => {
  locale.value = val
  localStorage.setItem('locale', val)
})
</script>

<template>
  <Select
    v-model="selected"
    class="h-8 w-auto cursor-pointer px-2"
  >
    <option v-for="opt in options" :key="opt.value" :value="opt.value">
      {{ opt.label }}
    </option>
  </Select>
</template>
