<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Plus } from 'lucide-vue-next'
import { Button } from '@/ui-v1/components/ui/button'
import { Input } from '@/ui-v1/components/ui/input'
import { Card } from '@/ui-v1/components/ui/card'

const props = defineProps<{
  globalFilter: string
}>()

const emit = defineEmits<{
  'update:globalFilter': [value: string]
  create: []
}>()

const { t } = useI18n()
</script>

<template>
  <div>
    <div class="flex flex-col justify-between gap-4 sm:flex-row sm:items-center">
      <div class="space-y-1">
        <h2 class="text-2xl font-bold tracking-tight">{{ t('admin.competitions.title') }}</h2>
        <p class="text-sm text-muted-foreground">{{ t('admin.competitions.subtitle') }}</p>
      </div>
      <Button class="shrink-0" @click="emit('create')">
        <Plus class="mr-2 size-4" />
        {{ t('admin.competitions.create') }}
      </Button>
    </div>

    <Card class="grid gap-3 p-3 md:grid-cols-[minmax(0,24rem)]">
      <div class="relative w-full max-w-sm">
        <Input :model-value="props.globalFilter" :placeholder="t('admin.competitions.searchPlaceholder')" class="pl-10" @update:model-value="emit('update:globalFilter', String($event))" />
      </div>
    </Card>
  </div>
</template>