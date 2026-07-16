<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Loader2 } from 'lucide-vue-next'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'

defineProps<{
  open: boolean
  title?: string
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
    <DialogContent>
      <DialogHeader>
        <DialogTitle>{{ t('admin.competitions.deleteDialogTitle') }}</DialogTitle>
        <DialogDescription>
          {{ t('admin.competitions.deleteDialogDescription') }}
        </DialogDescription>
      </DialogHeader>
      <div class="py-4">
        <p class="text-sm font-medium">
          {{ t('admin.competitions.deleteQuestion') }} <span class="font-bold text-foreground">"{{ title }}"</span>?
        </p>
      </div>
      <DialogFooter>
        <Button variant="outline" @click="emit('update:open', false)">
          {{ t('common.cancel') }}
        </Button>
        <Button variant="destructive" :disabled="deleting" @click="emit('confirm')">
          <Loader2 v-if="deleting" class="mr-2 size-4 animate-spin" />
          {{ t('common.delete') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>