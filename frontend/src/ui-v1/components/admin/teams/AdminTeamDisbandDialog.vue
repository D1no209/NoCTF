<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Loader2, Trash2 } from 'lucide-vue-next'
import { Button } from '@/ui-v1/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/ui-v1/components/ui/dialog'

interface TeamDto {
  id: string
  name: string
}

defineProps<{
  open: boolean
  team: TeamDto | null
  deleting: boolean
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  confirm: []
}>()

const { t } = useI18n()
</script>

<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-[425px]">
      <DialogHeader>
        <DialogTitle class="flex items-center gap-2 text-destructive">
          <Trash2 class="size-5" />
          {{ t('admin.teams.disbandDialogTitle') }}
        </DialogTitle>
        <DialogDescription>
          {{ t('admin.teams.disbandDialogDescription') }}
        </DialogDescription>
      </DialogHeader>
      <div class="py-4">
        <div class="rounded-lg border border-[var(--semantic-danger-border)] bg-[var(--semantic-danger-soft)] p-3 text-sm text-[var(--semantic-danger)]">
          <p class="font-medium">{{ t('admin.teams.disbandConfirmQuestion', { name: team?.name }) }}</p>
        </div>
      </div>
      <DialogFooter class="gap-2">
        <Button variant="outline" :disabled="deleting" @click="emit('update:open', false)">{{ t('common.cancel') }}</Button>
        <Button variant="destructive" :disabled="deleting" @click="emit('confirm')">
          <Loader2 v-if="deleting" class="mr-2 size-4 animate-spin" />
          {{ t('admin.teams.disband') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
