<script setup lang="ts">
import { Swords, Shield, TriangleAlert, Activity, LayoutGrid } from 'lucide-vue-next'

interface FilterItem {
  key: string
  label: string
  icon: any
}

const items: FilterItem[] = [
  { key: 'all', label: '全部', icon: LayoutGrid },
  { key: 'attack', label: '攻击', icon: Swords },
  { key: 'defense', label: '防御', icon: Shield },
  { key: 'service', label: '服务', icon: TriangleAlert },
  { key: 'system', label: '系统', icon: Activity },
]

const model = defineModel<string>('modelValue', { default: 'all' })

const emit = defineEmits<{
  change: [key: string]
}>()

function select(key: string) {
  model.value = key
  emit('change', key)
}
</script>

<template>
  <div class="flex flex-wrap items-center gap-1.5">
    <button
      v-for="item in items"
      :key="item.key"
      type="button"
      class="inline-flex items-center gap-1 rounded-md border px-2 py-1 text-[10px] font-extrabold uppercase tracking-wide transition-colors"
      :class="model === item.key
        ? 'border-[var(--semantic-info-border)] bg-[var(--semantic-info-soft)] text-[var(--semantic-info)]'
        : 'border-[var(--awdp-border)] bg-[var(--awdp-surface)] text-[var(--awdp-text-muted)] hover:border-[var(--semantic-neutral)] hover:text-[var(--awdp-text)]'"
      @click="select(item.key)"
    >
      <component :is="item.icon" class="size-3" />
      <span>{{ item.label }}</span>
    </button>
  </div>
</template>
