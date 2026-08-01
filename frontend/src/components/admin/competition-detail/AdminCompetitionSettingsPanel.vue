<script setup lang="ts">
import { Save } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Panel } from '@/components/ui/panel'
import { Textarea } from '@/components/ui/textarea'

defineProps<{
  competitionForm: {
    title: string
    description: string
    startTime: string
    endTime: string
    teamRegistrationAutoApprove: boolean
    maxTeamMembers: number
    maxConcurrentRuntimeInstancesPerTeam: number
  }
  saving: boolean
}>()

const emit = defineEmits<{ save: [] }>()
const { t } = useI18n()
</script>

<template>
  <Card class="space-y-5 p-4">
    <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
      <div>
        <h3 class="font-semibold">{{ t('admin.competitionDetail.settingsTitle') }}</h3>
        <p class="text-sm text-muted-foreground">{{ t('admin.competitionDetail.settingsDescription') }}</p>
      </div>
      <Button :disabled="saving" @click="emit('save')">
        <Save class="mr-2 size-4" />
        {{ t('common.save') }}
      </Button>
    </div>

    <div class="grid gap-4 md:grid-cols-2">
      <div class="grid gap-2 md:col-span-2">
        <Label for="competition-title">{{ t('admin.competitions.titleColumn') }}</Label>
        <Input id="competition-title" v-model="competitionForm.title" />
      </div>
      <div class="grid gap-2 md:col-span-2">
        <Label for="competition-description">{{ t('admin.competitions.description') }}</Label>
        <Textarea id="competition-description" v-model="competitionForm.description" rows="5" />
      </div>
      <div class="grid gap-2">
        <Label for="competition-start">{{ t('admin.competitions.startTime') }}</Label>
        <Input id="competition-start" v-model="competitionForm.startTime" type="datetime-local" />
      </div>
      <div class="grid gap-2">
        <Label for="competition-end">{{ t('admin.competitions.endTime') }}</Label>
        <Input id="competition-end" v-model="competitionForm.endTime" type="datetime-local" />
      </div>
      <div class="grid gap-2">
        <Label for="competition-max-members">{{ t('admin.competitionDetail.maxTeamMembers') }}</Label>
        <Input id="competition-max-members" v-model.number="competitionForm.maxTeamMembers" type="number" min="1" />
      </div>
      <div class="grid gap-2">
        <Label for="competition-runtime-quota">{{ t('admin.competitionDetail.maxConcurrentRuntimeInstancesPerTeam') }}</Label>
        <Input id="competition-runtime-quota" v-model.number="competitionForm.maxConcurrentRuntimeInstancesPerTeam" type="number" min="0" step="1" />
        <p class="text-xs text-muted-foreground">{{ t('admin.competitionDetail.maxConcurrentRuntimeInstancesPerTeamHint') }}</p>
      </div>
      <Panel class="md:col-span-2">
        <label class="flex min-h-10 cursor-pointer items-center gap-3 px-3 py-2 text-sm">
          <input v-model="competitionForm.teamRegistrationAutoApprove" type="checkbox" class="size-4">
          <span>{{ t('admin.competitionDetail.autoApproveTeams') }}</span>
        </label>
      </Panel>
    </div>
  </Card>
</template>
