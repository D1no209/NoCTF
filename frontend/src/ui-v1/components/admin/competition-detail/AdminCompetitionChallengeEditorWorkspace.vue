<script setup lang="ts">
import { ArrowLeft, Loader2, Plus, Save, Trash2 } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import DecayCurvePreview from '@/ui-v1/components/admin/DecayCurvePreview.vue'
import { useToastMutation } from '@/ui-v1/components/feedback/useToastMutation'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Button } from '@/ui-v1/components/ui/button'
import { Card, CardContent } from '@/ui-v1/components/ui/card'
import { Input } from '@/ui-v1/components/ui/input'
import { Label } from '@/ui-v1/components/ui/label'
import { Panel } from '@/ui-v1/components/ui/panel'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/ui-v1/components/ui/select'
import { Textarea } from '@/ui-v1/components/ui/textarea'
import { normalizeDirection } from '@/lib/challengeDirections'
import { useAdminCompetitionChallengeEditorPage } from '@/features/admin/useAdminCompetitionChallengeEditorPage'

const { t } = useI18n()

const {
  editing,
  form,
  competition,
  templates,
  selectedChallenge,
  directionOptions,
  loading,
  saveMutation: save,
  removeHint,
  returnToChallenges,
} = useAdminCompetitionChallengeEditorPage()

const saveMutation = useToastMutation(save, {
  success: () => t(editing.value ? 'admin.competitionDetail.updateChallengeSuccess' : 'admin.competitionDetail.deploySuccess'),
  error: () => t(editing.value ? 'admin.competitionDetail.updateChallengeError' : 'admin.competitionDetail.deployError'),
})
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6">
    <header class="flex flex-col gap-4 border-b pb-5 sm:flex-row sm:items-end sm:justify-between">
      <div>
        <Button variant="ghost" size="sm" class="-ml-2 mb-2" @click="returnToChallenges">
          <ArrowLeft class="mr-2 size-4" />
          {{ t('admin.competitionDetail.backToChallenges') }}
        </Button>
        <h2 class="text-2xl font-bold tracking-tight">
          {{ t(editing ? 'admin.competitionDetail.editCompetitionChallenge' : 'admin.competitionDetail.createCompetitionChallenge') }}
        </h2>
        <p class="text-sm text-muted-foreground">
          {{ competition?.title }} · {{ t('admin.competitionDetail.challengeEditorDescription') }}
        </p>
      </div>
      <Button :disabled="loading || saveMutation.isPending.value || (!editing && !form.templateId)" @click="saveMutation.mutate()">
        <Loader2 v-if="saveMutation.isPending.value" class="mr-2 size-4 animate-spin" />
        <Save v-else class="mr-2 size-4" />
        {{ t('common.save') }}
      </Button>
    </header>

    <div v-if="loading" class="flex min-h-80 items-center justify-center text-sm text-muted-foreground">
      <Loader2 class="mr-2 size-4 animate-spin" />
      {{ t('common.loading') }}
    </div>

    <template v-else>
      <Card class="p-0">
        <CardContent class="grid gap-5 p-5 lg:grid-cols-2">
          <div v-if="!editing" class="grid gap-2 lg:col-span-2">
            <Label>{{ t('admin.competitionDetail.challengeTemplate') }}</Label>
            <Select v-model="form.templateId">
              <SelectTrigger><SelectValue :placeholder="t('admin.competitionDetail.selectTemplate')" /></SelectTrigger>
              <SelectContent>
                <SelectItem v-for="template in templates ?? []" :key="template.id" :value="template.id">
                  {{ template.title }} · {{ template.typeId }} / {{ normalizeDirection(template.direction) }}
                </SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div v-else class="lg:col-span-2">
            <Badge variant="outline">{{ selectedChallenge?.title }}</Badge>
          </div>
          <div class="grid gap-2">
            <Label>{{ t('admin.challenges.direction') }}</Label>
            <Select v-model="form.direction">
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem v-for="item in directionOptions" :key="item" :value="item">{{ item }}</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div class="grid gap-2">
            <Label>{{ t('admin.competitionDetail.difficultyCoefficient') }}</Label>
            <Input v-model.number="form.difficultyCoefficient" type="number" min="0.1" step="0.1" />
          </div>
          <div class="grid gap-2">
            <Label>{{ t('admin.competitionDetail.flagPrefix') }}</Label>
            <Input v-model="form.flagPrefix" placeholder="flag" />
          </div>
          <Panel v-if="competition?.gameModeType === 'Ctf'">
            <label class="flex min-h-10 cursor-pointer items-center gap-3 px-3 py-2 text-sm">
              <input v-model="form.enableBloodBonus" type="checkbox" class="size-4">
              <span>{{ t('admin.competitionDetail.enableBloodBonus') }}</span>
            </label>
          </Panel>
          <div class="grid gap-2 lg:col-span-2">
            <Label>{{ t('admin.competitionDetail.markdownDescription') }}</Label>
            <Textarea v-model="form.description" class="font-mono text-xs" rows="8" :placeholder="t('admin.competitionDetail.descriptionPlaceholder')" />
          </div>
        </CardContent>
      </Card>

      <div class="grid gap-6 xl:grid-cols-[minmax(0,1fr)_360px]">
        <Card class="p-0">
          <CardContent class="space-y-5 p-5">
            <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
              <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.initial') }}</Label><Input v-model.number="form.initialPoints" type="number" /></div>
              <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.minimum') }}</Label><Input v-model.number="form.minimumPoints" type="number" /></div>
              <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.decayFactor') }}</Label><Input v-model.number="form.decayFactor" type="number" /></div>
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.decayFunction') }}</Label>
                <Select v-model="form.decayFunction">
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="sigmoid">{{ t('admin.competitionDetail.decaySigmoid') }}</SelectItem>
                    <SelectItem value="quadratic">{{ t('admin.competitionDetail.decayQuadratic') }}</SelectItem>
                    <SelectItem value="logarithmic">{{ t('admin.competitionDetail.decayLogarithmic') }}</SelectItem>
                    <SelectItem value="linear">{{ t('admin.competitionDetail.decayLinear') }}</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
            <DecayCurvePreview :config="form" />
          </CardContent>
        </Card>

        <Card class="p-0">
          <CardContent class="p-5">
            <div class="mb-4 flex items-center justify-between">
              <div>
                <h3 class="font-semibold">{{ t('admin.competitionDetail.hints') }}</h3>
                <p class="text-xs text-muted-foreground">{{ t('admin.competitionDetail.hintsDescription') }}</p>
              </div>
              <Button variant="outline" size="sm" @click="form.hints.push('')">
                <Plus class="mr-2 size-4" />
                {{ t('common.add') }}
              </Button>
            </div>
            <div class="space-y-3">
              <div v-for="(_, index) in form.hints" :key="index" class="flex items-center gap-2">
                <Input v-model="form.hints[index]" :placeholder="t('admin.competitionDetail.hintPlaceholder', { index: index + 1 })" />
                <Button variant="ghost" size="icon" class="shrink-0 text-destructive" @click="removeHint(index)">
                  <Trash2 class="size-4" />
                </Button>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <Card v-if="competition?.gameModeType === 'Awdp'" class="p-0">
        <CardContent class="grid gap-4 p-5 sm:grid-cols-2 lg:grid-cols-3">
          <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpAttackScore') }}</Label><Input v-model.number="form.awdpAttackScorePerRound" type="number" min="0" /></div>
          <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpDefenseScore') }}</Label><Input v-model.number="form.awdpDefenseScorePerRound" type="number" min="0" /></div>
          <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpMaxAttackAttempts') }}</Label><Input v-model.number="form.awdpMaxAttackAttempts" type="number" min="1" /></div>
          <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpMaxDefenseAttempts') }}</Label><Input v-model.number="form.awdpMaxDefenseAttempts" type="number" min="1" /></div>
          <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpFixEntry') }}</Label><Input v-model="form.awdpFixEntry" /></div>
          <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpFixTimeout') }}</Label><Input v-model.number="form.awdpFixTimeoutSeconds" type="number" min="1" /></div>
        </CardContent>
      </Card>

      <div class="flex justify-end gap-2 border-t pt-5">
        <Button variant="outline" @click="returnToChallenges">{{ t('common.cancel') }}</Button>
        <Button :disabled="saveMutation.isPending.value || (!editing && !form.templateId)" @click="saveMutation.mutate()">
          <Loader2 v-if="saveMutation.isPending.value" class="mr-2 size-4 animate-spin" />
          <Save v-else class="mr-2 size-4" />
          {{ t('common.save') }}
        </Button>
      </div>
    </template>
  </div>
</template>
