<script setup lang="ts">
import { ChevronDown, Globe2 } from 'lucide-vue-next'
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
  <label class="relative inline-flex items-center">
    <Globe2 class="pointer-events-none absolute left-3 size-4 text-muted-foreground" />
    <select
      v-model="selected"
      class="h-10 cursor-pointer appearance-none rounded-md border border-input bg-background/80 py-0 pl-9 pr-8 text-sm font-medium text-foreground shadow-none outline-none transition-all hover:bg-accent focus:border-primary focus:ring-4 focus:ring-primary/15"
    >
      <option v-for="opt in options" :key="opt.value" :value="opt.value">
        {{ opt.label }}
      </option>
    </select>
    <ChevronDown class="pointer-events-none absolute right-3 size-3.5 text-muted-foreground" />
  </label>
</template>
