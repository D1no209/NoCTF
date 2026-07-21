<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Loader2 } from 'lucide-vue-next'
import { Button } from '@/ui-v1/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/ui-v1/components/ui/dialog'
import { Input } from '@/ui-v1/components/ui/input'
import { Label } from '@/ui-v1/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/ui-v1/components/ui/select'

interface CompetitionForm {
  title: string
  description: string
  gameModeType: string
  startTime: string
  endTime: string
  status: string
}

const props = defineProps<{
  open: boolean
  isCreating: boolean
  form: CompetitionForm
  canSave: boolean
  saving: boolean
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  'update:form': [value: CompetitionForm]
  save: []
}>()

const { t } = useI18n()

function updateForm(patch: Partial<CompetitionForm>) {
  emit('update:form', { ...props.form, ...patch })
}
</script>

<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-[500px]">
      <DialogHeader>
        <DialogTitle>{{ isCreating ? t('admin.competitions.createDialogTitle') : t('admin.competitions.editDialogTitle') }}</DialogTitle>
        <DialogDescription>
          {{ isCreating ? t('admin.competitions.createDialogDescription') : t('admin.competitions.editDialogDescription') }}
        </DialogDescription>
      </DialogHeader>
      <div class="grid gap-4 py-4">
        <div class="grid gap-2">
          <Label for="title">{{ t('admin.competitions.titleColumn') }}</Label>
          <Input id="title" :model-value="form.title" :placeholder="t('admin.competitions.titleColumn')" @update:model-value="updateForm({ title: String($event) })" />
        </div>
        <div class="grid gap-2">
          <Label for="description">{{ t('admin.competitions.description') }}</Label>
          <Input id="description" :model-value="form.description" :placeholder="t('admin.competitions.description')" @update:model-value="updateForm({ description: String($event) })" />
        </div>
        <div class="grid grid-cols-2 gap-4">
          <div class="grid gap-2">
            <Label>{{ t('admin.competitions.gameMode') }}</Label>
            <Select :model-value="form.gameModeType" @update:model-value="updateForm({ gameModeType: String($event) })">
              <SelectTrigger>
                <SelectValue :placeholder="t('admin.competitions.selectModePlaceholder')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Ctf">{{ t('admin.competitions.modeCtf') }}</SelectItem>
                <SelectItem value="Awd">{{ t('admin.competitions.modeAwd') }}</SelectItem>
                <SelectItem value="Awdp">{{ t('admin.competitions.modeAwdp') }}</SelectItem>
                <SelectItem value="Koh">{{ t('admin.competitions.modeKoh') }}</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div class="grid gap-2">
            <Label>{{ t('admin.competitions.status') }}</Label>
            <Select :model-value="form.status" @update:model-value="updateForm({ status: String($event) })">
              <SelectTrigger>
                <SelectValue :placeholder="t('admin.competitions.selectStatusPlaceholder')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Draft">{{ t('competitions.status.draft') }}</SelectItem>
                <SelectItem value="Published">{{ t('competitions.status.published') }}</SelectItem>
                <SelectItem value="Running">{{ t('competitions.status.running') }}</SelectItem>
                <SelectItem value="Paused">{{ t('competitions.status.paused') }}</SelectItem>
                <SelectItem value="Finished">{{ t('competitions.status.finished') }}</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
        <div class="grid grid-cols-2 gap-4">
          <div class="grid gap-2">
            <Label>{{ t('admin.competitions.startTime') }}</Label>
            <Input :model-value="form.startTime" type="datetime-local" @update:model-value="updateForm({ startTime: String($event) })" />
          </div>
          <div class="grid gap-2">
            <Label>{{ t('admin.competitions.endTime') }}</Label>
            <Input :model-value="form.endTime" type="datetime-local" @update:model-value="updateForm({ endTime: String($event) })" />
          </div>
        </div>
      </div>
      <DialogFooter>
        <Button variant="outline" :disabled="saving" @click="emit('update:open', false)">
          {{ t('common.cancel') }}
        </Button>
        <Button :disabled="saving || !canSave" @click="emit('save')">
          <Loader2 v-if="saving" class="mr-2 size-4 animate-spin" />
          {{ isCreating ? t('common.create') : t('common.save') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>