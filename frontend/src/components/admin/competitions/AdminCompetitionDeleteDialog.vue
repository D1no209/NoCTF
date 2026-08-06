<script setup lang="ts">
import { Loader2 } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'

const props = defineProps<{
  open: boolean
  title?: string
  deleting: boolean
  mode: 'archive' | 'delete'
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  'confirm': []
}>()

const { t } = useI18n()
const titleKey = computed(() => props.mode === 'archive'
  ? 'admin.competitions.archiveDialogTitle'
  : 'admin.competitions.deleteDialogTitle')
const descriptionKey = computed(() => props.mode === 'archive'
  ? 'admin.competitions.archiveDialogDescription'
  : 'admin.competitions.deleteDialogDescription')
const questionKey = computed(() => props.mode === 'archive'
  ? 'admin.competitions.archiveQuestion'
  : 'admin.competitions.deleteQuestion')
const actionKey = computed(() => props.mode === 'archive'
  ? 'admin.competitions.archive'
  : 'admin.competitions.deletePermanently')
</script>

<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent>
      <DialogHeader>
        <DialogTitle>{{ t(titleKey) }}</DialogTitle>
        <DialogDescription>
          {{ t(descriptionKey) }}
        </DialogDescription>
      </DialogHeader>
      <div class="py-4">
        <p class="text-sm font-medium">
          {{ t(questionKey) }} <span class="font-bold text-foreground">"{{ title }}"</span>?
        </p>
      </div>
      <DialogFooter>
        <Button variant="outline" @click="emit('update:open', false)">
          {{ t('common.cancel') }}
        </Button>
        <Button variant="destructive" :disabled="deleting" @click="emit('confirm')">
          <Loader2 v-if="deleting" class="mr-2 size-4 animate-spin" />
          {{ t(actionKey) }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
