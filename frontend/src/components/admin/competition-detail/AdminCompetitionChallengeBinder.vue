<script setup lang="ts">
import DecayCurvePreview from '@/components/admin/DecayCurvePreview.vue'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Panel } from '@/components/ui/panel'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { Loader2, Plus } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'

defineProps<{
  bindForm: any
  templates: any[]
  competitionForm: any
  deploying: boolean
}>()

const emit = defineEmits<{
  addHint: []
  deploy: []
}>()

const { t } = useI18n()
</script>

<template>
  <Card class="p-4">
    <div class="mb-5">
      <h3 class="font-semibold">{{ t('admin.competitionDetail.deployTitle') }}</h3>
      <p class="text-sm text-muted-foreground">{{ t('admin.competitionDetail.deployDescription') }}</p>
    </div>

    <div class="grid gap-4 lg:grid-cols-2">
      <div class="grid gap-2">
        <Label>{{ t('admin.competitionDetail.challengeTemplate') }}</Label>
        <Select v-model="bindForm.templateId">
          <SelectTrigger><SelectValue :placeholder="t('admin.competitionDetail.selectTemplate')" /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="template in templates" :key="template.id" :value="template.id">
              {{ template.title }} · {{ template.typeId }} / {{ template.direction || 'Uncategorized' }}
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div class="grid gap-2">
        <Label>{{ t('admin.competitionDetail.difficultyCoefficient') }}</Label>
        <Input v-model.number="bindForm.difficultyCoefficient" type="number" min="0.1" step="0.1" />
      </div>
      <div class="grid gap-2">
        <Label>{{ t('admin.competitionDetail.flagPrefix') }}</Label>
        <Input v-model="bindForm.flagPrefix" placeholder="flag" />
      </div>
      <Panel v-if="competitionForm.gameModeType === 'Ctf'">
        <label class="flex cursor-pointer items-center gap-3 px-3 py-2 text-sm">
          <input v-model="bindForm.enableBloodBonus" type="checkbox" class="size-4">
          <span>{{ t('admin.competitionDetail.enableBloodBonus') }}</span>
        </label>
      </Panel>
      <div class="grid gap-2 lg:col-span-2">
        <Label>{{ t('admin.competitionDetail.markdownDescription') }}</Label>
        <Textarea v-model="bindForm.description" class="font-mono text-xs" rows="5" :placeholder="t('admin.competitionDetail.descriptionPlaceholder')" />
      </div>
    </div>

    <Card class="mt-4 p-0">
      <CardContent class="grid gap-4 lg:grid-cols-4 p-4">
        <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.initial') }}</Label><Input v-model.number="bindForm.initialPoints" type="number" /></div>
        <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.minimum') }}</Label><Input v-model.number="bindForm.minimumPoints" type="number" /></div>
        <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.decayFactor') }}</Label><Input v-model.number="bindForm.decayFactor" type="number" /></div>
        <div class="grid gap-2">
          <Label>{{ t('admin.competitionDetail.decayFunction') }}</Label>
          <Select v-model="bindForm.decayFunction">
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="sigmoid">{{ t('admin.competitionDetail.decaySigmoid') }}</SelectItem>
              <SelectItem value="quadratic">{{ t('admin.competitionDetail.decayQuadratic') }}</SelectItem>
              <SelectItem value="logarithmic">{{ t('admin.competitionDetail.decayLogarithmic') }}</SelectItem>
              <SelectItem value="linear">{{ t('admin.competitionDetail.decayLinear') }}</SelectItem>
            </SelectContent>
          </Select>
        </div>
      </CardContent>
    </Card>

    <Card v-if="competitionForm.gameModeType === 'Awdp'" class="mt-4 p-0">
      <CardContent class="grid gap-4 lg:grid-cols-3 p-4">
        <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpAttackScore') }}</Label><Input v-model.number="bindForm.awdpAttackScorePerRound" type="number" min="0" /></div>
        <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpDefenseScore') }}</Label><Input v-model.number="bindForm.awdpDefenseScorePerRound" type="number" min="0" /></div>
        <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpFixTimeout') }}</Label><Input v-model.number="bindForm.awdpFixTimeoutSeconds" type="number" min="1" /></div>
        <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpMaxAttackAttempts') }}</Label><Input v-model.number="bindForm.awdpMaxAttackAttempts" type="number" min="1" /></div>
        <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpMaxDefenseAttempts') }}</Label><Input v-model.number="bindForm.awdpMaxDefenseAttempts" type="number" min="1" /></div>
        <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpFixEntry') }}</Label><Input v-model="bindForm.awdpFixEntry" placeholder="fix.sh" /></div>
      </CardContent>
    </Card>

    <DecayCurvePreview class="mt-4" :config="bindForm" />

    <div class="mt-4 space-y-2">
      <div class="flex items-center justify-between">
        <Label>{{ t('admin.competitionDetail.hints') }}</Label>
        <Button variant="outline" size="sm" @click="emit('addHint')">
          <Plus class="mr-2 size-4" />
          {{ t('admin.competitionDetail.addHint') }}
        </Button>
      </div>
      <Input v-for="(_, index) in bindForm.hints" :key="index" v-model="bindForm.hints[index]" :placeholder="t('admin.competitionDetail.hintPlaceholder', { index: index + 1 })" />
    </div>

    <div class="mt-5 flex justify-end">
      <Button :disabled="deploying || !bindForm.templateId" @click="emit('deploy')">
        <Loader2 v-if="deploying" class="mr-2 size-4 animate-spin" />
        {{ t('admin.competitionDetail.deployChallenge') }}
      </Button>
    </div>
  </Card>
</template>
