<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Search } from 'lucide-vue-next'
import { Card } from '@/components/ui/card'

const props = defineProps<{
  filterUserName: string
  filterAction: string
  filterEntityType: string
}>()

const emit = defineEmits<{
  'update:filterUserName': [value: string]
  'update:filterAction': [value: string]
  'update:filterEntityType': [value: string]
  search: []
}>()

const { t } = useI18n()
</script>

<template>
  <Card class="grid gap-3 p-3 md:grid-cols-4 md:items-end">
    <div class="space-y-2">
      <label class="ml-1">{{ t('admin.auditLogs.user') }}</label>
      <div class="relative">
        <Search class="absolute left-3 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground" />
        <Input :model-value="props.filterUserName" :placeholder="t('admin.auditLogs.filterUser')" class="h-9 pl-9" @update:model-value="emit('update:filterUserName', String($event))" @keyup.enter="emit('search')" />
      </div>
    </div>
    <div class="space-y-2">
      <label class="ml-1">{{ t('admin.auditLogs.action') }}</label>
      <Input :model-value="props.filterAction" :placeholder="t('admin.auditLogs.filterAction')" class="h-9" @update:model-value="emit('update:filterAction', String($event))" @keyup.enter="emit('search')" />
    </div>
    <div class="space-y-2">
      <label class="ml-1">{{ t('admin.auditLogs.entityType') }}</label>
      <Input :model-value="props.filterEntityType" :placeholder="t('admin.auditLogs.filterEntity')" class="h-9" @update:model-value="emit('update:filterEntityType', String($event))" @keyup.enter="emit('search')" />
    </div>
    <Button class="h-9" @click="emit('search')">{{ t('common.search') }}</Button>
  </Card>
</template>