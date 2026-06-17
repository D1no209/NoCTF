<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

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
  <select
    v-model="selected"
    class="h-8 rounded-md border border-input bg-background px-2 text-sm shadow-sm focus:outline-none focus:ring-1 focus:ring-ring cursor-pointer"
  >
    <option v-for="opt in options" :key="opt.value" :value="opt.value">
      {{ opt.label }}
    </option>
  </select>
</template>
